import Foundation
import CryptoKit
import UniformTypeIdentifiers

enum TransferState: Equatable {
    case queued
    case running
    case completed
    case cancelled
    case failed(String)

    var isFinished: Bool {
        switch self {
        case .completed, .cancelled, .failed: return true
        case .queued, .running: return false
        }
    }
}

@MainActor
final class TransferTask: ObservableObject, Identifiable {
    enum Kind { case upload, download }

    let id = UUID()
    let kind: Kind
    let bucket: String
    let key: String
    let localURL: URL
    let client: S3Client

    @Published var state: TransferState = .queued
    @Published var transferredBytes: Int64 = 0
    @Published var totalBytes: Int64 = 0

    var runner: Task<Void, Never>?

    init(kind: Kind, bucket: String, key: String, localURL: URL, totalBytes: Int64 = 0, client: S3Client) {
        self.kind = kind
        self.bucket = bucket
        self.key = key
        self.localURL = localURL
        self.totalBytes = totalBytes
        self.client = client
    }

    var displayName: String {
        kind == .upload ? localURL.lastPathComponent : (key as NSString).lastPathComponent
    }

    var fractionCompleted: Double {
        totalBytes > 0 ? min(1, Double(transferredBytes) / Double(totalBytes)) : 0
    }

    var symbolName: String {
        kind == .upload ? "arrow.up.circle" : "arrow.down.circle"
    }
}

// MARK: - Resumable multipart records

/// On-disk record of an in-flight multipart upload, so it can be resumed after
/// a failure, cancellation, or app relaunch.
struct ResumableRecord: Codable {
    var uploadID: String
    var bucket: String
    var key: String
    var localPath: String
    var fileSize: Int64
    var partSize: Int64
    /// part number (as string) → ETag
    var completedParts: [String: String] = [:]
}

enum ResumableStore {
    static var directory: URL {
        AccountStore.supportDirectory.appendingPathComponent("resumable", isDirectory: true)
    }

    static func recordURL(bucket: String, key: String, localPath: String, fileSize: Int64) -> URL {
        let identity = "\(bucket)|\(key)|\(localPath)|\(fileSize)"
        let hash = SHA256.hash(data: Data(identity.utf8))
            .map { String(format: "%02x", $0) }.joined().prefix(32)
        return directory.appendingPathComponent("\(hash).json")
    }

    static func load(at url: URL) -> ResumableRecord? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(ResumableRecord.self, from: data)
    }

    static func save(_ record: ResumableRecord, at url: URL) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(record) {
            try? data.write(to: url, options: .atomic)
        }
    }

    static func delete(at url: URL) {
        try? FileManager.default.removeItem(at: url)
    }
}

// MARK: - Transfer manager

@MainActor
final class TransferManager: ObservableObject {
    @Published private(set) var tasks: [TransferTask] = []

    var maxConcurrent: Int {
        let value = UserDefaults.standard.integer(forKey: "maxConcurrentTransfers")
        return value > 0 ? value : 3
    }

    private var running = 0
    private var pending: [TransferTask] = []

    var activeCount: Int {
        tasks.filter { !$0.state.isFinished }.count
    }

    // MARK: Enqueue

    func enqueueUpload(fileURL: URL, bucket: String, key: String, client: S3Client) {
        let attributes = try? FileManager.default.attributesOfItem(atPath: fileURL.path)
        let size = (attributes?[.size] as? Int64) ?? 0
        let task = TransferTask(
            kind: .upload, bucket: bucket, key: key, localURL: fileURL,
            totalBytes: size, client: client
        )
        tasks.insert(task, at: 0)
        pending.append(task)
        pump()
    }

    func enqueueDownload(object: RemoteObject, bucket: String, to destination: URL, client: S3Client) {
        let task = TransferTask(
            kind: .download, bucket: bucket, key: object.key,
            localURL: destination, totalBytes: object.size, client: client
        )
        tasks.insert(task, at: 0)
        pending.append(task)
        pump()
    }

    func retry(_ task: TransferTask) {
        guard task.state.isFinished, task.state != .completed else { return }
        task.state = .queued
        task.transferredBytes = 0
        pending.append(task)
        pump()
    }

    func cancel(_ task: TransferTask) {
        if task.state == .queued {
            pending.removeAll { $0.id == task.id }
            task.state = .cancelled
        } else {
            task.runner?.cancel()
        }
    }

    func clearFinished() {
        tasks.removeAll { $0.state.isFinished }
    }

    // MARK: Scheduling

    private func pump() {
        while running < maxConcurrent, !pending.isEmpty {
            let task = pending.removeFirst()
            guard task.state == .queued else { continue }
            running += 1
            task.state = .running
            task.runner = Task { [weak self] in
                do {
                    switch task.kind {
                    case .upload:
                        try await Self.runUpload(task: task, client: task.client)
                    case .download:
                        try await Self.runDownload(task: task, client: task.client)
                    }
                    task.state = .completed
                    task.transferredBytes = task.totalBytes
                    if task.kind == .upload {
                        NotificationCenter.default.post(
                            name: .buckettTransferCompleted,
                            object: nil,
                            userInfo: ["bucket": task.bucket]
                        )
                    }
                } catch is CancellationError {
                    task.state = .cancelled
                } catch let urlError as URLError where urlError.code == .cancelled {
                    task.state = .cancelled
                } catch {
                    task.state = .failed(error.localizedDescription)
                }
                self?.running -= 1
                self?.pump()
            }
        }
    }

    // MARK: Download

    private nonisolated static func runDownload(task: TransferTask, client: S3Client) async throws {
        try await client.downloadObject(
            bucket: task.bucket,
            key: task.key,
            to: task.localURL
        ) { transferred, total in
            Task { @MainActor in
                task.transferredBytes = transferred
                if total > 0 { task.totalBytes = total }
            }
        }
    }

    // MARK: Upload

    private nonisolated static func mimeType(for url: URL) -> String {
        UTType(filenameExtension: url.pathExtension.lowercased())?.preferredMIMEType
            ?? "application/octet-stream"
    }

    private nonisolated static func runUpload(task: TransferTask, client: S3Client) async throws {
        let fileURL = task.localURL
        let attributes = try FileManager.default.attributesOfItem(atPath: fileURL.path)
        let fileSize = (attributes[.size] as? Int64) ?? 0
        await MainActor.run { task.totalBytes = fileSize }

        if fileSize >= S3Client.multipartThreshold {
            try await runMultipartUpload(task: task, client: client, fileSize: fileSize)
        } else {
            let data = try Data(contentsOf: fileURL)
            try await client.putObject(
                bucket: task.bucket,
                key: task.key,
                data: data,
                contentType: mimeType(for: fileURL)
            ) { sent, _ in
                Task { @MainActor in task.transferredBytes = sent }
            }
        }
    }

    /// Multipart upload that persists progress to disk. If a record for the same
    /// file + destination exists, previously completed parts are skipped.
    private nonisolated static func runMultipartUpload(
        task: TransferTask,
        client: S3Client,
        fileSize: Int64
    ) async throws {
        let bucket = task.bucket
        let key = task.key
        let fileURL = task.localURL
        let recordURL = ResumableStore.recordURL(
            bucket: bucket, key: key, localPath: fileURL.path, fileSize: fileSize
        )

        var record: ResumableRecord
        if let existing = ResumableStore.load(at: recordURL), existing.fileSize == fileSize {
            record = existing
        } else {
            let uploadID = try await client.createMultipartUpload(
                bucket: bucket, key: key, contentType: mimeType(for: fileURL)
            )
            record = ResumableRecord(
                uploadID: uploadID,
                bucket: bucket,
                key: key,
                localPath: fileURL.path,
                fileSize: fileSize,
                partSize: S3Client.multipartPartSize
            )
            ResumableStore.save(record, at: recordURL)
        }

        let partSize = record.partSize
        let partCount = Int((fileSize + partSize - 1) / partSize)
        let handle = try FileHandle(forReadingFrom: fileURL)
        defer { try? handle.close() }

        var completedBytes: Int64 = 0
        for part in 1...partCount where record.completedParts[String(part)] != nil {
            completedBytes += min(partSize, fileSize - Int64(part - 1) * partSize)
        }
        let baseCompleted = completedBytes
        await MainActor.run { task.transferredBytes = baseCompleted }

        for part in 1...partCount {
            try Task.checkCancellation()
            if record.completedParts[String(part)] != nil { continue }

            let offset = Int64(part - 1) * partSize
            let length = Int(min(partSize, fileSize - offset))
            try handle.seek(toOffset: UInt64(offset))
            guard let data = try handle.read(upToCount: length), data.count == length else {
                throw S3Error(status: 0, code: "ReadError", message: "Could not read part \(part)")
            }

            let alreadyDone = record.completedParts.keys
                .compactMap { Int($0) }
                .reduce(Int64(0)) { $0 + min(partSize, fileSize - Int64($1 - 1) * partSize) }

            let etag: String
            do {
                etag = try await client.uploadPart(
                    bucket: bucket, key: key, uploadID: record.uploadID,
                    partNumber: part, data: data
                ) { sent, _ in
                    Task { @MainActor in task.transferredBytes = alreadyDone + sent }
                }
            } catch let error as S3Error where error.isNoSuchUpload {
                // Stale record — the multipart upload no longer exists. Start over once.
                ResumableStore.delete(at: recordURL)
                let uploadID = try await client.createMultipartUpload(
                    bucket: bucket, key: key, contentType: mimeType(for: fileURL)
                )
                record = ResumableRecord(
                    uploadID: uploadID, bucket: bucket, key: key,
                    localPath: fileURL.path, fileSize: fileSize, partSize: partSize
                )
                ResumableStore.save(record, at: recordURL)
                try handle.seek(toOffset: UInt64(offset))
                etag = try await client.uploadPart(
                    bucket: bucket, key: key, uploadID: record.uploadID,
                    partNumber: part, data: data
                )
            }

            record.completedParts[String(part)] = etag
            ResumableStore.save(record, at: recordURL)
        }

        let parts = record.completedParts
            .compactMap { entry -> (partNumber: Int, eTag: String)? in
                guard let n = Int(entry.key) else { return nil }
                return (partNumber: n, eTag: entry.value)
            }
        try await client.completeMultipartUpload(
            bucket: bucket, key: key, uploadID: record.uploadID, parts: parts
        )
        ResumableStore.delete(at: recordURL)
    }
}
