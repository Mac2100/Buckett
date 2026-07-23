import Foundation
import CryptoKit

/// Minimal AWS Signature Version 4 signer — enough to talk to any S3-compatible API
/// (Cloudflare R2, Backblaze B2, AWS S3, MinIO, …) without pulling in an SDK.
enum SigV4 {

    struct Credentials {
        var accessKeyID: String
        var secretAccessKey: String
    }

    static let unreservedCharacters = CharacterSet(
        charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~"
    )
    static let unreservedWithSlash = unreservedCharacters.union(CharacterSet(charactersIn: "/"))

    /// RFC 3986 percent-encoding as required by SigV4.
    static func encode(_ string: String, keepSlash: Bool = false) -> String {
        string.addingPercentEncoding(
            withAllowedCharacters: keepSlash ? unreservedWithSlash : unreservedCharacters
        ) ?? string
    }

    static func sha256Hex(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    static let emptyPayloadHash = sha256Hex(Data())

    private static func hmac(key: Data, data: Data) -> Data {
        Data(HMAC<SHA256>.authenticationCode(for: data, using: SymmetricKey(data: key)))
    }

    private static func hmac(key: Data, string: String) -> Data {
        hmac(key: key, data: Data(string.utf8))
    }

    private static let amzDateFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd'T'HHmmss'Z'"
        f.timeZone = TimeZone(identifier: "UTC")
        f.locale = Locale(identifier: "en_US_POSIX")
        return f
    }()

    /// Signs `request` in place. The request URL's percent-encoded path and query must
    /// already be in canonical (SigV4) form — `S3Client` builds them that way.
    static func sign(
        request: inout URLRequest,
        payloadHash: String,
        credentials: Credentials,
        region: String,
        service: String = "s3",
        date: Date = Date()
    ) {
        guard let url = request.url,
              let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              let host = components.host
        else { return }

        let amzDate = amzDateFormatter.string(from: date)
        let dateStamp = String(amzDate.prefix(8))

        var hostHeader = host
        if let port = components.port, port != 443, port != 80 {
            hostHeader += ":\(port)"
        }

        request.setValue(amzDate, forHTTPHeaderField: "x-amz-date")
        request.setValue(payloadHash, forHTTPHeaderField: "x-amz-content-sha256")

        // Headers included in the signature: host, content-md5/content-type when present,
        // and every x-amz-* header on the request.
        var headersToSign: [String: String] = ["host": hostHeader]
        for (name, value) in request.allHTTPHeaderFields ?? [:] {
            let lower = name.lowercased()
            if lower.hasPrefix("x-amz-") || lower == "content-md5" || lower == "content-type" {
                headersToSign[lower] = value.trimmingCharacters(in: .whitespaces)
            }
        }

        let sortedNames = headersToSign.keys.sorted()
        let canonicalHeaders = sortedNames.map { "\($0):\(headersToSign[$0]!)\n" }.joined()
        let signedHeaders = sortedNames.joined(separator: ";")

        let canonicalURI = components.percentEncodedPath.isEmpty ? "/" : components.percentEncodedPath
        let canonicalQuery = components.percentEncodedQuery ?? ""

        let canonicalRequest = [
            request.httpMethod ?? "GET",
            canonicalURI,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadHash
        ].joined(separator: "\n")

        let scope = "\(dateStamp)/\(region)/\(service)/aws4_request"
        let stringToSign = [
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            sha256Hex(Data(canonicalRequest.utf8))
        ].joined(separator: "\n")

        let kDate = hmac(key: Data(("AWS4" + credentials.secretAccessKey).utf8), string: dateStamp)
        let kRegion = hmac(key: kDate, string: region)
        let kService = hmac(key: kRegion, string: service)
        let kSigning = hmac(key: kService, string: "aws4_request")
        let signature = hmac(key: kSigning, string: stringToSign)
            .map { String(format: "%02x", $0) }.joined()

        let authorization = "AWS4-HMAC-SHA256 " +
            "Credential=\(credentials.accessKeyID)/\(scope), " +
            "SignedHeaders=\(signedHeaders), " +
            "Signature=\(signature)"
        request.setValue(authorization, forHTTPHeaderField: "Authorization")
    }
}
