import Foundation
import UniformTypeIdentifiers

// MARK: - Provider

enum Provider: String, Codable, CaseIterable, Identifiable {
    case cloudflareR2 = "r2"
    case backblazeB2 = "b2"

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .cloudflareR2: return "Cloudflare R2"
        case .backblazeB2: return "Backblaze B2"
        }
    }

    var shortName: String {
        switch self {
        case .cloudflareR2: return "R2"
        case .backblazeB2: return "B2"
        }
    }

    var symbolName: String {
        switch self {
        case .cloudflareR2: return "cloud.fill"
        case .backblazeB2: return "flame.fill"
        }
    }

    var consoleURL: URL {
        switch self {
        case .cloudflareR2: return URL(string: "https://dash.cloudflare.com/?to=/:account/r2")!
        case .backblazeB2: return URL(string: "https://secure.backblaze.com/b2_buckets.htm")!
        }
    }
}

// MARK: - Account

/// Non-secret account configuration. The secret access key lives in the macOS Keychain,
/// keyed by `id` — see `Keychain.swift`.
struct Account: Codable, Identifiable, Hashable {
    var id: UUID = UUID()
    var name: String = ""
    var provider: Provider = .cloudflareR2
    /// Cloudflare account ID (R2 only) — used to derive the endpoint.
    var cloudflareAccountID: String = ""
    /// B2 region such as `us-west-004` — used to derive the endpoint.
    var b2Region: String = ""
    /// Optional custom S3-compatible endpoint; overrides the derived one when set.
    var customEndpoint: String = ""
    var accessKeyID: String = ""

    var signingRegion: String {
        switch provider {
        case .cloudflareR2: return "auto"
        case .backblazeB2: return b2Region.isEmpty ? "us-west-004" : b2Region
        }
    }

    var endpointURL: URL? {
        let custom = customEndpoint.trimmingCharacters(in: .whitespacesAndNewlines)
        if !custom.isEmpty {
            var raw = custom
            if !raw.contains("://") { raw = "https://" + raw }
            if raw.hasSuffix("/") { raw.removeLast() }
            return URL(string: raw)
        }
        switch provider {
        case .cloudflareR2:
            let acct = cloudflareAccountID.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !acct.isEmpty else { return nil }
            return URL(string: "https://\(acct).r2.cloudflarestorage.com")
        case .backblazeB2:
            let region = b2Region.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !region.isEmpty else { return nil }
            return URL(string: "https://s3.\(region).backblazeb2.com")
        }
    }

    var isConfigured: Bool {
        endpointURL != nil && !accessKeyID.isEmpty
    }
}

// MARK: - Bucket

struct Bucket: Identifiable, Hashable {
    var name: String
    var creationDate: Date?
    var id: String { name }
}

// MARK: - Remote object

/// A file or folder (common prefix) inside a bucket.
struct RemoteObject: Identifiable, Hashable {
    var key: String
    var size: Int64 = 0
    var lastModified: Date?
    var eTag: String?
    var storageClass: String?
    var isFolder: Bool = false

    var id: String { (isFolder ? "d:" : "f:") + key }

    /// Display name — the last path component of the key.
    var name: String {
        let trimmed = isFolder && key.hasSuffix("/") ? String(key.dropLast()) : key
        if let idx = trimmed.lastIndex(of: "/") {
            return String(trimmed[trimmed.index(after: idx)...])
        }
        return trimmed
    }

    var fileExtension: String {
        isFolder ? "" : (name as NSString).pathExtension.lowercased()
    }

    var sortDate: Date { lastModified ?? .distantPast }

    var contentType: UTType? {
        guard !fileExtension.isEmpty else { return nil }
        return UTType(filenameExtension: fileExtension)
    }

    var isImage: Bool { contentType?.conforms(to: .image) ?? false }
    var isVideo: Bool { contentType?.conforms(to: .movie) ?? false }
    var isAudio: Bool { contentType?.conforms(to: .audio) ?? false }
    var isText: Bool { contentType?.conforms(to: .text) ?? false }

    var symbolName: String {
        if isFolder { return "folder.fill" }
        if isImage { return "photo" }
        if isVideo { return "film" }
        if isAudio { return "waveform" }
        guard let type = contentType else { return "doc" }
        if type.conforms(to: .pdf) { return "doc.richtext" }
        if type.conforms(to: .archive) { return "doc.zipper" }
        if type.conforms(to: .sourceCode) || type.conforms(to: .json) || type.conforms(to: .xml) {
            return "chevron.left.forwardslash.chevron.right"
        }
        if type.conforms(to: .text) { return "doc.text" }
        return "doc"
    }

    var formattedSize: String {
        isFolder ? "—" : ByteCountFormatter.string(fromByteCount: size, countStyle: .file)
    }
}

// MARK: - Object metadata (HEAD)

struct ObjectMetadata {
    var contentType: String?
    var contentLength: Int64?
    var lastModified: String?
    var eTag: String?
    var storageClass: String?
    var custom: [String: String] = [:]   // x-amz-meta-*
}

// MARK: - Bucket analytics

struct ExtensionStat: Identifiable {
    var ext: String
    var count: Int
    var totalSize: Int64
    var id: String { ext }
}

struct BucketStats {
    var bucket: String
    var objectCount: Int
    var totalSize: Int64
    var byExtension: [ExtensionStat]
    var largestObjects: [RemoteObject]
    var newestModified: Date?
    var analyzedAt: Date

    var formattedSize: String {
        ByteCountFormatter.string(fromByteCount: totalSize, countStyle: .file)
    }
}

// MARK: - Browser display settings

enum ViewMode: String, CaseIterable, Identifiable {
    case grid, list
    var id: String { rawValue }
    var symbolName: String { self == .grid ? "square.grid.2x2" : "list.bullet" }
    var label: String { self == .grid ? "Grid" : "List" }
}

enum SortField: String, CaseIterable, Identifiable {
    case name, size, date, kind
    var id: String { rawValue }
    var label: String {
        switch self {
        case .name: return "Name"
        case .size: return "Size"
        case .date: return "Date Modified"
        case .kind: return "Kind"
        }
    }
}

// MARK: - Helpers

extension Int64 {
    var formattedBytes: String {
        ByteCountFormatter.string(fromByteCount: self, countStyle: .file)
    }
}
