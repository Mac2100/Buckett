using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Buckett.Services;

/// Minimal AWS Signature Version 4 signer — enough to talk to any S3-compatible API
/// (Cloudflare R2, Backblaze B2, AWS S3, MinIO, …) without pulling in an SDK.
public static class SigV4
{
    public readonly record struct Credentials(string AccessKeyID, string SecretAccessKey);

    private const string Unreserved =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    /// RFC 3986 percent-encoding as required by SigV4.
    public static string Encode(string value, bool keepSlash = false)
    {
        var builder = new StringBuilder(value.Length + 16);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (Unreserved.IndexOf(c) >= 0 || (keepSlash && c == '/'))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }
        return builder.ToString();
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256Hex(string value) => Sha256Hex(Encoding.UTF8.GetBytes(value));

    public static readonly string EmptyPayloadHash = Sha256Hex(ReadOnlySpan<byte>.Empty);

    private static byte[] Hmac(byte[] key, string data) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    public static string AmzDate(DateTime utc) =>
        utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static byte[] SigningKey(string secret, string dateStamp, string region, string service)
    {
        var kDate = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        var kRegion = Hmac(kDate, region);
        var kService = Hmac(kRegion, service);
        return Hmac(kService, "aws4_request");
    }

    /// Builds a presigned GET/PUT URL (SigV4 query-string auth) valid for `expires` seconds.
    /// `canonicalPath` must already be in canonical percent-encoded form.
    public static string? Presign(
        string scheme,
        string host,
        int port,
        string canonicalPath,
        Credentials credentials,
        string region,
        int expires,
        string method = "GET",
        string service = "s3",
        DateTime? date = null)
    {
        var utc = (date ?? DateTime.UtcNow).ToUniversalTime();
        var amzDate = AmzDate(utc);
        var dateStamp = amzDate[..8];
        var scope = $"{dateStamp}/{region}/{service}/aws4_request";

        var hostHeader = host;
        if (port != 443 && port != 80 && port > 0) hostHeader += ":" + port;

        var query = new List<(string Name, string Value)>
        {
            ("X-Amz-Algorithm", "AWS4-HMAC-SHA256"),
            ("X-Amz-Credential", $"{credentials.AccessKeyID}/{scope}"),
            ("X-Amz-Date", amzDate),
            ("X-Amz-Expires", expires.ToString(CultureInfo.InvariantCulture)),
            ("X-Amz-SignedHeaders", "host")
        };

        var canonicalQuery = string.Join("&", query
            .Select(pair => (Name: Encode(pair.Name), Value: Encode(pair.Value)))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .Select(pair => $"{pair.Name}={pair.Value}"));

        var canonicalUri = canonicalPath.Length == 0 ? "/" : canonicalPath;
        var canonicalRequest = string.Join("\n",
            method,
            canonicalUri,
            canonicalQuery,
            $"host:{hostHeader}\n",
            "host",
            "UNSIGNED-PAYLOAD");

        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Sha256Hex(canonicalRequest));

        var signature = Convert
            .ToHexString(Hmac(SigningKey(credentials.SecretAccessKey, dateStamp, region, service), stringToSign))
            .ToLowerInvariant();

        query.Add(("X-Amz-Signature", signature));
        var finalQuery = string.Join("&", query.Select(pair => $"{Encode(pair.Name)}={Encode(pair.Value)}"));

        var portPart = port is 443 or 80 or <= 0 ? "" : ":" + port;
        return $"{scheme}://{host}{portPart}{canonicalUri}?{finalQuery}";
    }

    /// Signs `request` in place. The request URI's percent-encoded path and query must
    /// already be in canonical (SigV4) form — `S3Client` builds them that way.
    public static void Sign(
        HttpRequestMessage request,
        string canonicalPath,
        string canonicalQuery,
        string payloadHash,
        Credentials credentials,
        string region,
        string service = "s3",
        DateTime? date = null)
    {
        var uri = request.RequestUri;
        if (uri == null) return;

        var utc = (date ?? DateTime.UtcNow).ToUniversalTime();
        var amzDate = AmzDate(utc);
        var dateStamp = amzDate[..8];

        var hostHeader = uri.Host;
        if (!uri.IsDefaultPort) hostHeader += ":" + uri.Port;

        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

        // Headers included in the signature: host, content-md5/content-type when present,
        // and every x-amz-* header on the request.
        var headersToSign = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = hostHeader
        };

        void Collect(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            foreach (var header in headers)
            {
                var lower = header.Key.ToLowerInvariant();
                if (lower.StartsWith("x-amz-", StringComparison.Ordinal) ||
                    lower is "content-md5" or "content-type")
                {
                    headersToSign[lower] = string.Join(",", header.Value).Trim();
                }
            }
        }

        Collect(request.Headers);
        if (request.Content != null) Collect(request.Content.Headers);

        var canonicalHeaders = string.Concat(headersToSign.Select(kv => $"{kv.Key}:{kv.Value}\n"));
        var signedHeaders = string.Join(";", headersToSign.Keys);

        var canonicalUri = canonicalPath.Length == 0 ? "/" : canonicalPath;
        var canonicalRequest = string.Join("\n",
            request.Method.Method,
            canonicalUri,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadHash);

        var scope = $"{dateStamp}/{region}/{service}/aws4_request";
        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Sha256Hex(canonicalRequest));

        var signature = Convert
            .ToHexString(Hmac(SigningKey(credentials.SecretAccessKey, dateStamp, region, service), stringToSign))
            .ToLowerInvariant();

        var authorization =
            "AWS4-HMAC-SHA256 " +
            $"Credential={credentials.AccessKeyID}/{scope}, " +
            $"SignedHeaders={signedHeaders}, " +
            $"Signature={signature}";
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }
}
