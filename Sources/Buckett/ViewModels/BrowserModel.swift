import Foundation
import SwiftUI

extension Notification.Name {
    static let buckettTransferCompleted = Notification.Name("buckettTransferCompleted")
}

/// State + actions for browsing a single bucket.
@MainActor
final class BrowserModel: ObservableObject {
    let bucket: String
    let client: S3Client
    let transfers: TransferManager

    @Published var prefix: String = "" {
        didSet { selection.removeAll() }
    }
    @Published var folders: [RemoteObject] = []
    @Published var files: [RemoteObject] = []
    @Published var isLoading = false
    @Published var isBusy = false
    @Published var errorMessage: String?
    @Published var filterText = ""
    @Published var viewMode: ViewMode
    @Published var sortField: SortField = .name
    @Published var sortAscending = true
    @Published var selection = Set<String>()
    @Published var isTruncated = false

    private var continuationToken: String?
    private var observer: NSObjectProtocol?
    private var refreshDebounce: Task<Void, Never>?

    init(bucket: String, client: S3Client, transfers: TransferManager) {
        self.bucket = bucket
        self.client = client
        self.transfers = transfers
        self.viewMode = ViewMode(
            rawValue: UserDefaults.standard.string(forKey: "defaultViewMode") ?? ""
        ) ?? .grid

        observer = NotificationCenter.default.addObserver(
            forName: .buckettTransferCompleted, object: nil, queue: .main
        ) { [weak self] notification in
            guard let bucketName = notification.userInfo?["bucket"] as? String else { return }
            Task { @MainActor [weak self] in
                guard let self, bucketName == self.bucket else { return }
                self.scheduleRefresh()
            }
        }
    }

    deinit {
        if let observer {
            NotificationCenter.default.removeObserver(observer)
        }
    }

    // MARK: - Display

    var displayItems: [RemoteObject] {
        let query = filterText.trimmingCharacters(in: .whitespaces)
        var shownFolders = folders
        var shownFiles = files
        if !query.isEmpty {
            shownFolders = shownFolders.filter { $0.name.localizedCaseInsensitiveContains(query) }
            shownFiles = shownFiles.filter { $0.name.localizedCaseInsensitiveContains(query) }
        }
        return sorted(shownFolders) + sorted(shownFiles)
    }

    var selectedObjects: [RemoteObject] {
        displayItems.filter { selection.contains($0.id) }
    }

    private func sorted(_ items: [RemoteObject]) -> [RemoteObject] {
        let base: [RemoteObject]
        switch sortField {
        case .name:
            base = items.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        case .size:
            base = items.sorted { $0.size < $1.size }
        case .date:
            base = items.sorted { $0.sortDate < $1.sortDate }
        case .kind:
            base = items.sorted {
                if $0.fileExtension != $1.fileExtension {
                    return $0.fileExtension < $1.fileExtension
                }
                return $0.name.localizedStandardCompare($1.name) == .orderedAscending
            }
        }
        return sortAscending ? base : base.reversed()
    }

    var breadcrumbs: [(name: String, prefix: String)] {
        var crumbs: [(String, String)] = [(bucket, "")]
        var running = ""
        for component in prefix.split(separator: "/") {
            running += component + "/"
            crumbs.append((String(component), running))
        }
        return crumbs
    }

    // MARK: - Loading

    func load() async {
        isLoading = true
        errorMessage = nil
        do {
            let result = try await client.listObjects(bucket: bucket, prefix: prefix)
            folders = result.folders
            files = result.objects
            isTruncated = result.isTruncated
            continuationToken = result.nextContinuationToken
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    func loadMore() async {
        guard let token = continuationToken else { return }
        do {
            let result = try await client.listObjects(
                bucket: bucket, prefix: prefix, continuationToken: token
            )
            folders.append(contentsOf: result.folders.filter { f in
                !folders.contains(where: { $0.key == f.key })
            })
            files.append(contentsOf: result.objects)
            isTruncated = result.isTruncated
            continuationToken = result.nextContinuationToken
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func refresh() async {
        await load()
    }

    /// Coalesces bursts of transfer-completed notifications into one reload.
    private func scheduleRefresh() {
        refreshDebounce?.cancel()
        refreshDebounce = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 700_000_000)
            guard !Task.isCancelled else { return }
            await self?.load()
        }
    }

    // MARK: - Navigation

    func open(folder: RemoteObject) {
        guard folder.isFolder else { return }
        prefix = folder.key
        Task { await load() }
    }

    func navigate(toPrefix newPrefix: String) {
        prefix = newPrefix
        Task { await load() }
    }

    func navigateUp() {
        guard !prefix.isEmpty else { return }
        var components = prefix.split(separator: "/")
        components.removeLast()
        navigate(toPrefix: components.isEmpty ? "" : components.joined(separator: "/") + "/")
    }

    // MARK: - Uploads

    /// Expands directories recursively into (fileURL, key suffix relative to current prefix).
    nonisolated static func expandForUpload(urls: [URL]) -> [(URL, String)] {
        var result: [(URL, String)] = []
        let fm = FileManager.default
        for url in urls {
            var isDirectory: ObjCBool = false
            guard fm.fileExists(atPath: url.path, isDirectory: &isDirectory) else { continue }
            if isDirectory.boolValue {
                let baseName = url.lastPathComponent
                guard let enumerator = fm.enumerator(
                    at: url,
                    includingPropertiesForKeys: [.isRegularFileKey],
                    options: [.skipsHiddenFiles]
                ) else { continue }
                for case let child as URL in enumerator {
                    let values = try? child.resourceValues(forKeys: [.isRegularFileKey])
                    guard values?.isRegularFile == true else { continue }
                    let relative = child.path
                        .replacingOccurrences(of: url.path, with: "")
                        .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
                    result.append((child, baseName + "/" + relative))
                }
            } else {
                result.append((url, url.lastPathComponent))
            }
        }
        return result
    }

    func upload(urls: [URL]) {
        let currentPrefix = prefix
        Task {
            let expanded = await Task.detached(priority: .userInitiated) {
                BrowserModel.expandForUpload(urls: urls)
            }.value
            for (fileURL, suffix) in expanded {
                transfers.enqueueUpload(
                    fileURL: fileURL,
                    bucket: bucket,
                    key: currentPrefix + suffix,
                    client: client
                )
            }
        }
    }

    // MARK: - Downloads

    func download(objects: [RemoteObject], to directory: URL) {
        Task {
            for object in objects {
                if object.isFolder {
                    do {
                        let children = try await client.listAllObjects(
                            bucket: bucket, prefix: object.key
                        )
                        let parentPrefix = prefix
                        for child in children where !child.key.hasSuffix("/") {
                            let relative = String(child.key.dropFirst(parentPrefix.count))
                            let destination = directory.appendingPathComponent(relative)
                            transfers.enqueueDownload(
                                object: child, bucket: bucket, to: destination, client: client
                            )
                        }
                    } catch {
                        errorMessage = error.localizedDescription
                    }
                } else {
                    let destination = directory.appendingPathComponent(object.name)
                    transfers.enqueueDownload(
                        object: object, bucket: bucket, to: destination, client: client
                    )
                }
            }
        }
    }

    // MARK: - Delete

    func delete(objects: [RemoteObject]) async {
        isBusy = true
        defer { isBusy = false }
        do {
            var keys: [String] = []
            for object in objects {
                if object.isFolder {
                    let children = try await client.listAllObjects(bucket: bucket, prefix: object.key)
                    keys.append(contentsOf: children.map(\.key))
                    keys.append(object.key) // folder marker, if any
                } else {
                    keys.append(object.key)
                }
            }
            try await client.deleteObjects(bucket: bucket, keys: Array(Set(keys)))
            selection.removeAll()
            await load()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Rename (copy + delete)

    func rename(object: RemoteObject, to newName: String) async {
        let trimmed = newName.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty, trimmed != object.name, !trimmed.contains("/") else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            if object.isFolder {
                let newPrefix = prefix + trimmed + "/"
                let children = try await client.listAllObjects(bucket: bucket, prefix: object.key)
                for child in children {
                    let suffix = String(child.key.dropFirst(object.key.count))
                    try await client.copyObject(
                        bucket: bucket, fromKey: child.key, toKey: newPrefix + suffix
                    )
                }
                try await client.deleteObjects(bucket: bucket, keys: children.map(\.key))
            } else {
                let newKey = prefix + trimmed
                try await client.copyObject(bucket: bucket, fromKey: object.key, toKey: newKey)
                try await client.deleteObject(bucket: bucket, key: object.key)
            }
            await load()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Batch rename: find & replace within the names of the selected files.
    func batchRename(find: String, replace: String) async {
        guard !find.isEmpty else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            for object in selectedObjects where !object.isFolder {
                let newName = object.name.replacingOccurrences(of: find, with: replace)
                guard newName != object.name, !newName.isEmpty, !newName.contains("/") else { continue }
                let newKey = prefix + newName
                try await client.copyObject(bucket: bucket, fromKey: object.key, toKey: newKey)
                try await client.deleteObject(bucket: bucket, key: object.key)
            }
            selection.removeAll()
            await load()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Folders

    func createFolder(named name: String) async {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
            .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard !trimmed.isEmpty else { return }
        do {
            try await client.putObject(bucket: bucket, key: prefix + trimmed + "/", data: Data())
            await load()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    // MARK: - Metadata

    func metadata(for object: RemoteObject) async -> ObjectMetadata? {
        do {
            return try await client.headObject(bucket: bucket, key: object.key)
        } catch {
            errorMessage = error.localizedDescription
            return nil
        }
    }
}
