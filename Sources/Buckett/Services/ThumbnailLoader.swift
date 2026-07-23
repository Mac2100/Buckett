import AppKit
import Foundation

/// Downloads and caches small image thumbnails for the grid view.
actor ThumbnailLoader {
    static let shared = ThumbnailLoader()

    /// Images larger than this are not fetched for thumbnails.
    static let maxBytes: Int64 = 15 * 1024 * 1024
    static let thumbnailSide: CGFloat = 256

    private let cache = NSCache<NSString, NSImage>()
    private var inFlight: [String: Task<NSImage?, Never>] = [:]

    private init() {
        cache.countLimit = 500
    }

    func thumbnail(for object: RemoteObject, bucket: String, client: S3Client) async -> NSImage? {
        guard object.isImage, object.size > 0, object.size <= Self.maxBytes else { return nil }
        let cacheKey = "\(bucket)/\(object.key)/\(object.eTag ?? "")" as NSString

        if let cached = cache.object(forKey: cacheKey) {
            return cached
        }
        if let existing = inFlight[cacheKey as String] {
            return await existing.value
        }

        let task = Task<NSImage?, Never> {
            guard let data = try? await client.getObjectData(bucket: bucket, key: object.key),
                  let image = NSImage(data: data)
            else { return nil }
            return Self.downscale(image, to: Self.thumbnailSide)
        }
        inFlight[cacheKey as String] = task
        let image = await task.value
        inFlight[cacheKey as String] = nil
        if let image {
            cache.setObject(image, forKey: cacheKey)
        }
        return image
    }

    private static func downscale(_ image: NSImage, to side: CGFloat) -> NSImage {
        let size = image.size
        guard size.width > side || size.height > side, size.width > 0, size.height > 0 else {
            return image
        }
        let scale = min(side / size.width, side / size.height)
        let newSize = NSSize(width: size.width * scale, height: size.height * scale)
        let result = NSImage(size: newSize)
        result.lockFocus()
        image.draw(
            in: NSRect(origin: .zero, size: newSize),
            from: NSRect(origin: .zero, size: size),
            operation: .copy,
            fraction: 1
        )
        result.unlockFocus()
        return result
    }
}
