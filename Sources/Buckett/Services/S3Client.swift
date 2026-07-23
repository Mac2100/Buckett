import Foundation
import CryptoKit

struct S3Error: LocalizedError {
    var status: Int
    var code: String?
    var message: String?

    var errorDescription: String? {
        var parts: [String] = ["HTTP \(status)"]
        if let code, !code.isEmpty { parts.append(code) }
        if let message, !message.isEmpty { parts.append(message) }
        return parts.joined(separator: " — ")
    }

    var isNoSuchUpload: Bool { code == "NoSuchUpload" }
}

struct ListResult {
    var objects: [RemoteObject]
    var folders: [RemoteObject]
    var isTruncated: Bool
    var nextContinuationToken: String?
}

/// S3-compatible REST client (Cloudflare R2, Backblaze B2, AWS S3, MinIO…)
/// using path-style addressing and SigV4 request signing.
final class S3Client {
    let endpoint: URL
    let region: String
    let credentials: SigV4.Credentials
    private let session: URLSession

    static let multipartPartSize: Int64 = 8 * 1024 * 1024
    static let multipartThreshold: Int64 = 16 * 1024 * 1024

    init(endpoint: URL, region: String, credentials: SigV4.Credentials) {
        self.endpoint = endpoint
        self.region = region
        self.credentials = credentials
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 120
        config.timeoutIntervalForResource = 24 * 60 * 60
        config.httpMaximumConnectionsPerHost = 8
        self.session = URLSession(configuration: config)
    }

    convenience init?(account: Account, secretKey: String) {
        guard let endpoint = account.endpointURL else { return nil }
        self.init(
            endpoint: endpoint,
            region: account.signingRegion,
            credentials: .init(accessKeyID: account.accessKeyID, secretAccessKey: secretKey)
        )
    }

    // MARK: - Request plumbing

    private func buildRequest(
        method: String,
        bucket: String? = nil,
        key: String? = nil,
        query: [(String, String?)] = [],
        headers: [String: String] = [:],
        body: Data? = nil
    ) throws -> URLRequest {
        guard var components = URLComponents(url: endpoint, resolvingAgainstBaseURL: false) else {
            throw S3Error(status: 0, code: "BadEndpoint", message: endpoint.absoluteString)
        }

        var path = components.percentEncodedPath
        if path.hasSuffix("/") { path.removeLast() }
        if let bucket {
            path += "/" + SigV4.encode(bucket)
            if let key {
                path += "/" + SigV4.encode(key, keepSlash: true)
            }
        }
        components.percentEncodedPath = path.isEmpty ? "/" : path

        if !query.isEmpty {
            let canonical = query
                .map { (SigV4.encode($0.0), SigV4.encode($0.1 ?? "")) }
                .sorted { $0.0 < $1.0 }
                .map { "\($0.0)=\($0.1)" }
                .joined(separator: "&")
            components.percentEncodedQuery = canonical
        }

        guard let url = components.url else {
            throw S3Error(status: 0, code: "BadURL", message: components.string)
        }

        var request = URLRequest(url: url)
        request.httpMethod = method
        for (name, value) in headers {
            request.setValue(value, forHTTPHeaderField: name)
        }
        request.httpBody = body

        let payloadHash = SigV4.sha256Hex(body ?? Data())
        SigV4.sign(
            request: &request,
            payloadHash: payloadHash,
            credentials: credentials,
            region: region
        )
        return request
    }

    private func check(_ data: Data, _ response: URLResponse) throws -> (Data, HTTPURLResponse) {
        guard let http = response as? HTTPURLResponse else {
            throw S3Error(status: 0, code: "NoResponse", message: nil)
        }
        guard (200..<300).contains(http.statusCode) else {
            let root = XMLTree.parse(data)
            throw S3Error(
                status: http.statusCode,
                code: root?["Code"]?.trimmedText,
                message: root?["Message"]?.trimmedText
            )
        }
        return (data, http)
    }

    @discardableResult
    private func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let (data, response) = try await session.data(for: request)
        return try check(data, response)
    }

    /// Runs a data/upload task with byte-level progress reported via the task's Progress object.
    private func perform(
        _ request: URLRequest,
        progress: (@Sendable (Int64, Int64) -> Void)?
    ) async throws -> (Data, HTTPURLResponse) {
        let task = SessionTaskBox()
        return try await withTaskCancellationHandler {
            let (data, response): (Data, URLResponse) = try await withCheckedThrowingContinuation { continuation in
                let dataTask = session.dataTask(with: request) { data, response, error in
                    if let error {
                        continuation.resume(throwing: error)
                    } else {
                        continuation.resume(returning: (data ?? Data(), response ?? URLResponse()))
                    }
                }
                if let progress {
                    task.observation = dataTask.progress.observe(\.fractionCompleted) { p, _ in
                        progress(p.completedUnitCount, p.totalUnitCount)
                    }
                }
                task.task = dataTask
                dataTask.resume()
            }
            return try check(data, response)
        } onCancel: {
            task.cancel()
        }
    }

    /// Downloads straight to a file on disk with progress.
    private func performDownload(
        _ request: URLRequest,
        to destination: URL,
        progress: (@Sendable (Int64, Int64) -> Void)?
    ) async throws {
        let task = SessionTaskBox()
        try await withTaskCancellationHandler {
            let response: URLResponse = try await withCheckedThrowingContinuation { continuation in
                let downloadTask = session.downloadTask(with: request) { tempURL, response, error in
                    if let error {
                        continuation.resume(throwing: error)
                        return
                    }
                    guard let tempURL, let response else {
                        continuation.resume(throwing: S3Error(status: 0, code: "NoResponse", message: nil))
                        return
                    }
                    do {
                        let fm = FileManager.default
                        try fm.createDirectory(
                            at: destination.deletingLastPathComponent(),
                            withIntermediateDirectories: true
                        )
                        if fm.fileExists(atPath: destination.path) {
                            try fm.removeItem(at: destination)
                        }
                        try fm.moveItem(at: tempURL, to: destination)
                        continuation.resume(returning: response)
                    } catch {
                        continuation.resume(throwing: error)
                    }
                }
                if let progress {
                    task.observation = downloadTask.progress.observe(\.fractionCompleted) { p, _ in
                        progress(p.completedUnitCount, p.totalUnitCount)
                    }
                }
                task.task = downloadTask
                downloadTask.resume()
            }
            guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                try? FileManager.default.removeItem(at: destination)
                throw S3Error(status: status, code: nil, message: "Download failed")
            }
        } onCancel: {
            task.cancel()
        }
    }

    // MARK: - Buckets

    func listBuckets() async throws -> [Bucket] {
        let request = try buildRequest(method: "GET")
        let (data, _) = try await send(request)
        guard let root = XMLTree.parse(data) else { return [] }
        let buckets = root["Buckets"]?.all("Bucket") ?? []
        return buckets.compactMap { node in
            guard let name = node["Name"]?.trimmedText, !name.isEmpty else { return nil }
            let created = node["CreationDate"]?.trimmedText
            return Bucket(name: name, creationDate: created.flatMap(S3Date.parse))
        }
    }

    func createBucket(_ name: String) async throws {
        let request = try buildRequest(method: "PUT", bucket: name)
        try await send(request)
    }

    func deleteBucket(_ name: String) async throws {
        let request = try buildRequest(method: "DELETE", bucket: name)
        try await send(request)
    }

    // MARK: - Listing

    func listObjects(
        bucket: String,
        prefix: String = "",
        delimiter: String? = "/",
        continuationToken: String? = nil,
        maxKeys: Int = 1000
    ) async throws -> ListResult {
        var query: [(String, String?)] = [
            ("list-type", "2"),
            ("max-keys", String(maxKeys))
        ]
        if !prefix.isEmpty { query.append(("prefix", prefix)) }
        if let delimiter { query.append(("delimiter", delimiter)) }
        if let continuationToken { query.append(("continuation-token", continuationToken)) }

        let request = try buildRequest(method: "GET", bucket: bucket, query: query)
        let (data, _) = try await send(request)
        guard let root = XMLTree.parse(data) else {
            return ListResult(objects: [], folders: [], isTruncated: false, nextContinuationToken: nil)
        }

        var objects: [RemoteObject] = []
        for node in root.all("Contents") {
            guard let key = node["Key"]?.trimmedText, !key.isEmpty else { continue }
            // Skip the zero-byte placeholder for the current "folder" itself.
            if key == prefix, key.hasSuffix("/") { continue }
            var object = RemoteObject(key: key)
            object.size = Int64(node["Size"]?.trimmedText ?? "") ?? 0
            object.lastModified = (node["LastModified"]?.trimmedText).flatMap(S3Date.parse)
            object.eTag = node["ETag"]?.trimmedText.replacingOccurrences(of: "\"", with: "")
            object.storageClass = node["StorageClass"]?.trimmedText
            object.isFolder = key.hasSuffix("/") && object.size == 0
            objects.append(object)
        }

        var folders: [RemoteObject] = []
        for node in root.all("CommonPrefixes") {
            guard let p = node["Prefix"]?.trimmedText, !p.isEmpty else { continue }
            folders.append(RemoteObject(key: p, isFolder: true))
        }

        // Zero-byte "folder marker" objects show up as folders too.
        let markerFolders = objects.filter { $0.isFolder }
        objects.removeAll { $0.isFolder }
        for marker in markerFolders where !folders.contains(where: { $0.key == marker.key }) {
            folders.append(marker)
        }

        let truncated = root["IsTruncated"]?.trimmedText == "true"
        return ListResult(
            objects: objects,
            folders: folders,
            isTruncated: truncated,
            nextContinuationToken: root["NextContinuationToken"]?.trimmedText
        )
    }

    /// Recursively lists every object under a prefix (no delimiter), following pagination.
    func listAllObjects(bucket: String, prefix: String = "") async throws -> [RemoteObject] {
        var all: [RemoteObject] = []
        var token: String? = nil
        repeat {
            try Task.checkCancellation()
            let page = try await listObjects(
                bucket: bucket,
                prefix: prefix,
                delimiter: nil,
                continuationToken: token
            )
            all.append(contentsOf: page.objects)
            token = page.isTruncated ? page.nextContinuationToken : nil
        } while token != nil
        return all
    }

    // MARK: - Objects

    func getObjectData(bucket: String, key: String) async throws -> Data {
        let request = try buildRequest(method: "GET", bucket: bucket, key: key)
        let (data, _) = try await send(request)
        return data
    }

    func downloadObject(
        bucket: String,
        key: String,
        to destination: URL,
        progress: (@Sendable (Int64, Int64) -> Void)? = nil
    ) async throws {
        let request = try buildRequest(method: "GET", bucket: bucket, key: key)
        try await performDownload(request, to: destination, progress: progress)
    }

    func putObject(
        bucket: String,
        key: String,
        data: Data,
        contentType: String? = nil,
        progress: (@Sendable (Int64, Int64) -> Void)? = nil
    ) async throws {
        var headers: [String: String] = [:]
        if let contentType { headers["Content-Type"] = contentType }
        let request = try buildRequest(
            method: "PUT", bucket: bucket, key: key, headers: headers, body: data
        )
        _ = try await perform(request, progress: progress)
    }

    func deleteObject(bucket: String, key: String) async throws {
        let request = try buildRequest(method: "DELETE", bucket: bucket, key: key)
        try await send(request)
    }

    /// Batch delete, chunked into the S3 limit of 1000 keys per request.
    func deleteObjects(bucket: String, keys: [String]) async throws {
        var remaining = keys
        while !remaining.isEmpty {
            let chunk = Array(remaining.prefix(1000))
            remaining.removeFirst(chunk.count)

            let objectsXML = chunk
                .map { "<Object><Key>\(xmlEscape($0))</Key></Object>" }
                .joined()
            let body = Data("<Delete><Quiet>true</Quiet>\(objectsXML)</Delete>".utf8)
            let md5 = Data(Insecure.MD5.hash(data: body)).base64EncodedString()

            let request = try buildRequest(
                method: "POST",
                bucket: bucket,
                query: [("delete", nil)],
                headers: ["Content-MD5": md5, "Content-Type": "application/xml"],
                body: body
            )
            let (data, _) = try await send(request)
            if let root = XMLTree.parse(data), root.name == "DeleteResult",
               let error = root.all("Error").first {
                throw S3Error(
                    status: 200,
                    code: error["Code"]?.trimmedText,
                    message: error["Message"]?.trimmedText
                )
            }
        }
    }

    func copyObject(bucket: String, fromKey: String, toKey: String) async throws {
        let source = "/" + SigV4.encode(bucket) + "/" + SigV4.encode(fromKey, keepSlash: true)
        let request = try buildRequest(
            method: "PUT",
            bucket: bucket,
            key: toKey,
            headers: ["x-amz-copy-source": source]
        )
        let (data, _) = try await send(request)
        // A 200 response can still carry an error body for CopyObject.
        if let root = XMLTree.parse(data), root.name == "Error" {
            throw S3Error(
                status: 200,
                code: root["Code"]?.trimmedText,
                message: root["Message"]?.trimmedText
            )
        }
    }

    func headObject(bucket: String, key: String) async throws -> ObjectMetadata {
        let request = try buildRequest(method: "HEAD", bucket: bucket, key: key)
        let (_, http) = try await send(request)

        var meta = ObjectMetadata()
        meta.contentType = http.value(forHTTPHeaderField: "Content-Type")
        meta.contentLength = http.value(forHTTPHeaderField: "Content-Length").flatMap { Int64($0) }
        meta.lastModified = http.value(forHTTPHeaderField: "Last-Modified")
        meta.eTag = http.value(forHTTPHeaderField: "ETag")?
            .replacingOccurrences(of: "\"", with: "")
        meta.storageClass = http.value(forHTTPHeaderField: "x-amz-storage-class")
        for (name, value) in http.allHeaderFields {
            guard let name = name as? String, let value = value as? String else { continue }
            let lower = name.lowercased()
            if lower.hasPrefix("x-amz-meta-") {
                meta.custom[String(lower.dropFirst("x-amz-meta-".count))] = value
            }
        }
        return meta
    }

    /// Time-limited shareable URL for an object (SigV4 query-string auth).
    func presignedURL(bucket: String, key: String, expires: TimeInterval) -> URL? {
        guard var components = URLComponents(url: endpoint, resolvingAgainstBaseURL: false) else {
            return nil
        }
        var path = components.percentEncodedPath
        if path.hasSuffix("/") { path.removeLast() }
        path += "/" + SigV4.encode(bucket) + "/" + SigV4.encode(key, keepSlash: true)
        components.percentEncodedPath = path
        guard let url = components.url else { return nil }
        return SigV4.presign(
            url: url,
            credentials: credentials,
            region: region,
            expires: Int(expires)
        )
    }

    /// True if an object with this exact key exists.
    func objectExists(bucket: String, key: String) async throws -> Bool {
        do {
            _ = try await headObject(bucket: bucket, key: key)
            return true
        } catch let error as S3Error where error.status == 404 {
            return false
        }
    }

    // MARK: - Multipart upload

    func createMultipartUpload(
        bucket: String,
        key: String,
        contentType: String? = nil
    ) async throws -> String {
        var headers: [String: String] = [:]
        if let contentType { headers["Content-Type"] = contentType }
        let request = try buildRequest(
            method: "POST", bucket: bucket, key: key, query: [("uploads", nil)], headers: headers
        )
        let (data, _) = try await send(request)
        guard let uploadID = XMLTree.parse(data)?["UploadId"]?.trimmedText, !uploadID.isEmpty else {
            throw S3Error(status: 200, code: "NoUploadId", message: "Missing UploadId in response")
        }
        return uploadID
    }

    /// Uploads one part; returns its ETag.
    func uploadPart(
        bucket: String,
        key: String,
        uploadID: String,
        partNumber: Int,
        data: Data,
        progress: (@Sendable (Int64, Int64) -> Void)? = nil
    ) async throws -> String {
        let request = try buildRequest(
            method: "PUT",
            bucket: bucket,
            key: key,
            query: [("partNumber", String(partNumber)), ("uploadId", uploadID)],
            body: data
        )
        let (_, http) = try await perform(request, progress: progress)
        guard let etag = http.value(forHTTPHeaderField: "ETag") else {
            throw S3Error(status: http.statusCode, code: "NoETag", message: "Missing part ETag")
        }
        return etag.replacingOccurrences(of: "\"", with: "")
    }

    func completeMultipartUpload(
        bucket: String,
        key: String,
        uploadID: String,
        parts: [(partNumber: Int, eTag: String)]
    ) async throws {
        let partsXML = parts
            .sorted { $0.partNumber < $1.partNumber }
            .map {
                "<Part><PartNumber>\($0.partNumber)</PartNumber><ETag>\"\(xmlEscape($0.eTag))\"</ETag></Part>"
            }
            .joined()
        let body = Data("<CompleteMultipartUpload>\(partsXML)</CompleteMultipartUpload>".utf8)
        let request = try buildRequest(
            method: "POST",
            bucket: bucket,
            key: key,
            query: [("uploadId", uploadID)],
            headers: ["Content-Type": "application/xml"],
            body: body
        )
        let (data, _) = try await send(request)
        // S3 can return 200 with an error body for CompleteMultipartUpload.
        if let root = XMLTree.parse(data), root.name == "Error" {
            throw S3Error(
                status: 200,
                code: root["Code"]?.trimmedText,
                message: root["Message"]?.trimmedText
            )
        }
    }

    func abortMultipartUpload(bucket: String, key: String, uploadID: String) async throws {
        let request = try buildRequest(
            method: "DELETE", bucket: bucket, key: key, query: [("uploadId", uploadID)]
        )
        try await send(request)
    }
}

/// Holds a URLSession task + progress observation so cancellation can reach it
/// from the task-cancellation handler.
private final class SessionTaskBox: @unchecked Sendable {
    private let lock = NSLock()
    private var _task: URLSessionTask?
    private var _observation: NSKeyValueObservation?
    private var cancelled = false

    var task: URLSessionTask? {
        get { lock.lock(); defer { lock.unlock() }; return _task }
        set {
            lock.lock()
            _task = newValue
            let shouldCancel = cancelled
            lock.unlock()
            if shouldCancel { newValue?.cancel() }
        }
    }

    var observation: NSKeyValueObservation? {
        get { lock.lock(); defer { lock.unlock() }; return _observation }
        set { lock.lock(); _observation = newValue; lock.unlock() }
    }

    func cancel() {
        lock.lock()
        cancelled = true
        let t = _task
        lock.unlock()
        t?.cancel()
    }
}
