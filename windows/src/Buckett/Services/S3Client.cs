using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Buckett.Models;

namespace Buckett.Services;

public sealed class S3Exception : Exception
{
    public S3Exception(int status, string? code, string? message)
        : base(Describe(status, code, message))
    {
        Status = status;
        Code = code;
        S3Message = message;
    }

    public int Status { get; }
    public string? Code { get; }
    public string? S3Message { get; }

    public bool IsNoSuchUpload => Code == "NoSuchUpload";

    private static string Describe(int status, string? code, string? message)
    {
        var parts = new List<string> { $"HTTP {status}" };
        if (!string.IsNullOrEmpty(code)) parts.Add(code!);
        if (!string.IsNullOrEmpty(message)) parts.Add(message!);
        return string.Join(" — ", parts);
    }
}

public sealed class ListResult
{
    public required List<RemoteObject> Objects { get; init; }
    public required List<RemoteObject> Folders { get; init; }
    public required bool IsTruncated { get; init; }
    public string? NextContinuationToken { get; init; }
}

public readonly record struct ObjectVersion(string Key, string? VersionID);

public readonly record struct MultipartUploadRef(string Key, string UploadID);

public readonly record struct CompletedPart(int PartNumber, string ETag);

/// S3-compatible REST client (Cloudflare R2, Backblaze B2, AWS S3, MinIO…)
/// using path-style addressing and SigV4 request signing.
public sealed class S3Client
{
    public const long MultipartPartSize = 8 * 1024 * 1024;
    public const long MultipartThreshold = 16 * 1024 * 1024;

    private static readonly HttpClient Http = CreateHttpClient();

    public S3Client(Uri endpoint, string region, SigV4.Credentials credentials)
    {
        Endpoint = endpoint;
        Region = region;
        Credentials = credentials;
    }

    public Uri Endpoint { get; }
    public string Region { get; }
    public SigV4.Credentials Credentials { get; }

    public static S3Client? Create(Account account, string secretKey)
    {
        var endpoint = account.EndpointUrl;
        if (endpoint == null) return null;
        return new S3Client(
            endpoint,
            account.SigningRegion,
            new SigV4.Credentials(account.AccessKeyID, secretKey));
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 8,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true
        };
        return new HttpClient(handler)
        {
            // Per-operation cancellation is used instead of a global timeout so
            // multi-hour transfers are not cut short.
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    // MARK: - Request plumbing

    private sealed record PreparedRequest(HttpRequestMessage Message);

    private string BasePath()
    {
        var path = Endpoint.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        path = path.TrimEnd('/');
        return path.Length == 0 ? "" : "/" + path.TrimStart('/');
    }

    private string CanonicalPath(string? bucket, string? key)
    {
        var path = BasePath();
        if (bucket != null)
        {
            path += "/" + SigV4.Encode(bucket);
            if (key != null)
            {
                path += "/" + SigV4.Encode(key, keepSlash: true);
            }
        }
        return path.Length == 0 ? "/" : path;
    }

    private static string CanonicalQuery(IReadOnlyList<(string Name, string? Value)> query)
    {
        if (query.Count == 0) return "";
        return string.Join("&", query
            .Select(pair => (Name: SigV4.Encode(pair.Name), Value: SigV4.Encode(pair.Value ?? "")))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .Select(pair => $"{pair.Name}={pair.Value}"));
    }

    private PreparedRequest BuildRequest(
        HttpMethod method,
        string? bucket = null,
        string? key = null,
        IReadOnlyList<(string Name, string? Value)>? query = null,
        IReadOnlyDictionary<string, string>? headers = null,
        HttpContent? content = null,
        string? payloadHash = null)
    {
        var canonicalPath = CanonicalPath(bucket, key);
        var canonicalQuery = CanonicalQuery(query ?? Array.Empty<(string, string?)>());

        var portPart = Endpoint.IsDefaultPort ? "" : ":" + Endpoint.Port;
        var url = $"{Endpoint.Scheme}://{Endpoint.Host}{portPart}{canonicalPath}";
        if (canonicalQuery.Length > 0) url += "?" + canonicalQuery;

        // Canonicalization must stay off: S3 keys may legitimately contain
        // sequences ("." / ".." / "//") that .NET would otherwise rewrite,
        // which would break both the signature and the addressed object.
        var uri = new Uri(url, new UriCreationOptions
        {
            DangerousDisablePathAndQueryCanonicalization = true
        });

        var message = new HttpRequestMessage(method, uri);
        if (content != null) message.Content = content;

        if (headers != null)
        {
            foreach (var (name, value) in headers)
            {
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    // Content-Type only travels on the body. Requests that carry a
                    // type but no payload (CreateMultipartUpload) get an empty body
                    // so the header is actually sent — otherwise it would be part
                    // of the signature but missing from the wire, and the provider
                    // would reject the request.
                    message.Content ??= new ByteArrayContent(Array.Empty<byte>());
                    message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
                }
                else if (name.Equals("Content-MD5", StringComparison.OrdinalIgnoreCase) && message.Content != null)
                {
                    message.Content.Headers.TryAddWithoutValidation("Content-MD5", value);
                }
                else
                {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        SigV4.Sign(
            message,
            canonicalPath,
            canonicalQuery,
            payloadHash ?? SigV4.EmptyPayloadHash,
            Credentials,
            Region);

        return new PreparedRequest(message);
    }

    private static async Task<(byte[] Body, HttpResponseMessage Response)> CheckAsync(
        HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        if ((int)response.StatusCode is < 200 or >= 300)
        {
            var root = XmlTree.Parse(body);
            throw new S3Exception(
                (int)response.StatusCode,
                root?.TextOf("Code"),
                root?.TextOf("Message"));
        }
        return (body, response);
    }

    private async Task<(byte[] Body, HttpResponseMessage Response)> SendAsync(
        PreparedRequest prepared, CancellationToken token = default)
    {
        var response = await Http
            .SendAsync(prepared.Message, HttpCompletionOption.ResponseContentRead, token)
            .ConfigureAwait(false);
        return await CheckAsync(response, token).ConfigureAwait(false);
    }

    // MARK: - Buckets

    public async Task<List<Bucket>> ListBucketsAsync(CancellationToken token = default)
    {
        var (body, _) = await SendAsync(BuildRequest(HttpMethod.Get), token).ConfigureAwait(false);
        var root = XmlTree.Parse(body);
        if (root == null) return new List<Bucket>();

        var result = new List<Bucket>();
        var buckets = root["Buckets"]?.All("Bucket") ?? (IReadOnlyList<XmlNode>)Array.Empty<XmlNode>();
        foreach (var node in buckets)
        {
            var name = node.TextOf("Name");
            if (string.IsNullOrEmpty(name)) continue;
            result.Add(new Bucket(name!, S3Date.Parse(node.TextOf("CreationDate"))));
        }
        return result;
    }

    public async Task CreateBucketAsync(string name, CancellationToken token = default) =>
        await SendAsync(BuildRequest(HttpMethod.Put, name), token).ConfigureAwait(false);

    public async Task DeleteBucketAsync(string name, CancellationToken token = default) =>
        await SendAsync(BuildRequest(HttpMethod.Delete, name), token).ConfigureAwait(false);

    // MARK: - Listing

    public async Task<ListResult> ListObjectsAsync(
        string bucket,
        string prefix = "",
        string? delimiter = "/",
        string? continuationToken = null,
        int maxKeys = 1000,
        CancellationToken token = default)
    {
        var query = new List<(string, string?)>
        {
            ("list-type", "2"),
            ("max-keys", maxKeys.ToString(CultureInfo.InvariantCulture))
        };
        if (prefix.Length > 0) query.Add(("prefix", prefix));
        if (delimiter != null) query.Add(("delimiter", delimiter));
        if (continuationToken != null) query.Add(("continuation-token", continuationToken));

        var (body, _) = await SendAsync(BuildRequest(HttpMethod.Get, bucket, query: query), token)
            .ConfigureAwait(false);

        var root = XmlTree.Parse(body);
        if (root == null)
        {
            return new ListResult
            {
                Objects = new List<RemoteObject>(),
                Folders = new List<RemoteObject>(),
                IsTruncated = false
            };
        }

        var objects = new List<RemoteObject>();
        foreach (var node in root.All("Contents"))
        {
            var key = node.TextOf("Key");
            if (string.IsNullOrEmpty(key)) continue;
            // Skip the zero-byte placeholder for the current "folder" itself.
            if (key == prefix && key!.EndsWith("/")) continue;

            var size = long.TryParse(node.TextOf("Size"), out var parsedSize) ? parsedSize : 0;
            objects.Add(new RemoteObject
            {
                Key = key!,
                Size = size,
                LastModified = S3Date.Parse(node.TextOf("LastModified")),
                ETag = node.TextOf("ETag")?.Replace("\"", ""),
                StorageClass = node.TextOf("StorageClass"),
                IsFolder = key!.EndsWith("/") && size == 0
            });
        }

        var folders = new List<RemoteObject>();
        foreach (var node in root.All("CommonPrefixes"))
        {
            var p = node.TextOf("Prefix");
            if (string.IsNullOrEmpty(p)) continue;
            folders.Add(new RemoteObject { Key = p!, IsFolder = true });
        }

        // Zero-byte "folder marker" objects show up as folders too.
        var markerFolders = objects.Where(o => o.IsFolder).ToList();
        objects.RemoveAll(o => o.IsFolder);
        foreach (var marker in markerFolders)
        {
            if (folders.All(f => f.Key != marker.Key)) folders.Add(marker);
        }

        return new ListResult
        {
            Objects = objects,
            Folders = folders,
            IsTruncated = root.TextOf("IsTruncated") == "true",
            NextContinuationToken = root.TextOf("NextContinuationToken")
        };
    }

    /// Recursively lists every object under a prefix (no delimiter), following pagination.
    public async Task<List<RemoteObject>> ListAllObjectsAsync(
        string bucket, string prefix = "", CancellationToken token = default)
    {
        var all = new List<RemoteObject>();
        string? continuation = null;
        do
        {
            token.ThrowIfCancellationRequested();
            var page = await ListObjectsAsync(
                bucket, prefix, delimiter: null, continuationToken: continuation, token: token)
                .ConfigureAwait(false);
            all.AddRange(page.Objects);
            continuation = page.IsTruncated ? page.NextContinuationToken : null;
        } while (continuation != null);
        return all;
    }

    // MARK: - Objects

    public async Task<byte[]> GetObjectDataAsync(
        string bucket, string key, CancellationToken token = default)
    {
        var (body, _) = await SendAsync(BuildRequest(HttpMethod.Get, bucket, key), token)
            .ConfigureAwait(false);
        return body;
    }

    /// Downloads straight to a file on disk with progress.
    public async Task DownloadObjectAsync(
        string bucket,
        string key,
        string destination,
        Action<long, long>? progress = null,
        CancellationToken token = default)
    {
        var prepared = BuildRequest(HttpMethod.Get, bucket, key);
        using var response = await Http
            .SendAsync(prepared.Message, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);

        if ((int)response.StatusCode is < 200 or >= 300)
        {
            var body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            var root = XmlTree.Parse(body);
            throw new S3Exception(
                (int)response.StatusCode,
                root?.TextOf("Code"),
                root?.TextOf("Message") ?? "Download failed");
        }

        var total = response.Content.Headers.ContentLength ?? 0;
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = destination + ".buckett-part";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var file = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                var buffer = new byte[128 * 1024];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    written += read;
                    progress?.Invoke(written, total);
                }
            }

            if (File.Exists(destination)) File.Delete(destination);
            File.Move(temporary, destination);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* best effort */ }
            throw;
        }
    }

    public async Task PutObjectAsync(
        string bucket,
        string key,
        byte[] data,
        string? contentType = null,
        Action<long, long>? progress = null,
        CancellationToken token = default)
    {
        var headers = new Dictionary<string, string>();
        if (contentType != null) headers["Content-Type"] = contentType;

        HttpContent content = progress == null
            ? new ByteArrayContent(data)
            : new ProgressByteArrayContent(data, progress);

        var prepared = BuildRequest(
            HttpMethod.Put, bucket, key,
            headers: headers,
            content: content,
            payloadHash: SigV4.Sha256Hex(data));

        await SendAsync(prepared, token).ConfigureAwait(false);
    }

    public async Task DeleteObjectAsync(string bucket, string key, CancellationToken token = default) =>
        await SendAsync(BuildRequest(HttpMethod.Delete, bucket, key), token).ConfigureAwait(false);

    /// Batch delete, chunked into the S3 limit of 1000 keys per request.
    public async Task DeleteObjectsAsync(
        string bucket, IReadOnlyList<string> keys, CancellationToken token = default)
    {
        var remaining = keys.ToList();
        while (remaining.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var chunk = remaining.Take(1000).ToList();
            remaining.RemoveRange(0, chunk.Count);

            var objectsXml = string.Concat(
                chunk.Select(key => $"<Object><Key>{S3Xml.Escape(key)}</Key></Object>"));
            var body = Encoding.UTF8.GetBytes($"<Delete><Quiet>true</Quiet>{objectsXml}</Delete>");

            await SendDeleteBodyAsync(bucket, body, token).ConfigureAwait(false);
        }
    }

    private async Task SendDeleteBodyAsync(string bucket, byte[] body, CancellationToken token)
    {
        var md5 = Convert.ToBase64String(MD5.HashData(body));
        var prepared = BuildRequest(
            HttpMethod.Post,
            bucket,
            query: new List<(string, string?)> { ("delete", null) },
            headers: new Dictionary<string, string>
            {
                ["Content-MD5"] = md5,
                ["Content-Type"] = "application/xml"
            },
            content: new ByteArrayContent(body),
            payloadHash: SigV4.Sha256Hex(body));

        var (responseBody, _) = await SendAsync(prepared, token).ConfigureAwait(false);
        var root = XmlTree.Parse(responseBody);
        if (root is { Name: "DeleteResult" })
        {
            var error = root.All("Error").FirstOrDefault();
            if (error != null)
            {
                throw new S3Exception(200, error.TextOf("Code"), error.TextOf("Message"));
            }
        }
    }

    public Task CopyObjectAsync(
        string bucket, string fromKey, string toKey, CancellationToken token = default) =>
        CopyObjectAsync(bucket, fromKey, bucket, toKey, token);

    /// Server-side copy, including across buckets in the same account.
    public async Task CopyObjectAsync(
        string fromBucket,
        string fromKey,
        string toBucket,
        string toKey,
        CancellationToken token = default)
    {
        var source = "/" + SigV4.Encode(fromBucket) + "/" + SigV4.Encode(fromKey, keepSlash: true);
        var prepared = BuildRequest(
            HttpMethod.Put, toBucket, toKey,
            headers: new Dictionary<string, string> { ["x-amz-copy-source"] = source });

        var (body, _) = await SendAsync(prepared, token).ConfigureAwait(false);
        // A 200 response can still carry an error body for CopyObject.
        var root = XmlTree.Parse(body);
        if (root is { Name: "Error" })
        {
            throw new S3Exception(200, root.TextOf("Code"), root.TextOf("Message"));
        }
    }

    /// Uploads a local file, using multipart for large files (no resume record —
    /// used for cross-account relays; interactive uploads go through TransferManager).
    public async Task UploadFileAsync(
        string bucket,
        string key,
        string filePath,
        string? contentType = null,
        CancellationToken token = default)
    {
        var fileSize = new FileInfo(filePath).Length;

        if (fileSize < MultipartThreshold)
        {
            var data = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
            await PutObjectAsync(bucket, key, data, contentType, token: token).ConfigureAwait(false);
            return;
        }

        var uploadID = await CreateMultipartUploadAsync(bucket, key, contentType, token)
            .ConfigureAwait(false);
        try
        {
            await using var handle = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);

            var partCount = (int)((fileSize + MultipartPartSize - 1) / MultipartPartSize);
            var parts = new List<CompletedPart>();
            for (var part = 1; part <= partCount; part++)
            {
                token.ThrowIfCancellationRequested();
                var offset = (long)(part - 1) * MultipartPartSize;
                var length = (int)Math.Min(MultipartPartSize, fileSize - offset);
                handle.Seek(offset, SeekOrigin.Begin);
                var data = new byte[length];
                await handle.ReadExactlyAsync(data, token).ConfigureAwait(false);

                var etag = await UploadPartAsync(bucket, key, uploadID, part, data, token: token)
                    .ConfigureAwait(false);
                parts.Add(new CompletedPart(part, etag));
            }
            await CompleteMultipartUploadAsync(bucket, key, uploadID, parts, token).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await AbortMultipartUploadAsync(bucket, key, uploadID, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch { /* best effort */ }
            throw;
        }
    }

    // MARK: - Object versions & multipart housekeeping (bucket emptying)

    /// Every object version and delete marker in the bucket — what actually has
    /// to go before a versioning provider (e.g. Backblaze B2) will delete it.
    public async Task<List<ObjectVersion>> ListAllObjectVersionsAsync(
        string bucket, CancellationToken token = default)
    {
        var all = new List<ObjectVersion>();
        string? keyMarker = null;
        string? versionMarker = null;
        var more = true;

        while (more)
        {
            token.ThrowIfCancellationRequested();
            var query = new List<(string, string?)> { ("versions", null), ("max-keys", "1000") };
            if (keyMarker != null) query.Add(("key-marker", keyMarker));
            if (versionMarker != null) query.Add(("version-id-marker", versionMarker));

            var (body, _) = await SendAsync(BuildRequest(HttpMethod.Get, bucket, query: query), token)
                .ConfigureAwait(false);
            var root = XmlTree.Parse(body);
            if (root == null) break;

            foreach (var node in root.All("Version").Concat(root.All("DeleteMarker")))
            {
                var key = node.TextOf("Key");
                if (string.IsNullOrEmpty(key)) continue;
                var versionID = node.TextOf("VersionId");
                all.Add(new ObjectVersion(
                    key!,
                    !string.IsNullOrEmpty(versionID) && versionID != "null" ? versionID : null));
            }

            var truncated = root.TextOf("IsTruncated") == "true";
            keyMarker = root.TextOf("NextKeyMarker");
            versionMarker = root.TextOf("NextVersionIdMarker");
            more = truncated && (keyMarker != null || versionMarker != null);
        }
        return all;
    }

    /// Batch-deletes specific object versions (chunked at the S3 limit of 1000).
    public async Task DeleteObjectVersionsAsync(
        string bucket, IReadOnlyList<ObjectVersion> versions, CancellationToken token = default)
    {
        var remaining = versions.ToList();
        while (remaining.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var chunk = remaining.Take(1000).ToList();
            remaining.RemoveRange(0, chunk.Count);

            var objectsXml = string.Concat(chunk.Select(version =>
            {
                var xml = $"<Object><Key>{S3Xml.Escape(version.Key)}</Key>";
                if (version.VersionID != null)
                {
                    xml += $"<VersionId>{S3Xml.Escape(version.VersionID)}</VersionId>";
                }
                return xml + "</Object>";
            }));
            var body = Encoding.UTF8.GetBytes($"<Delete><Quiet>true</Quiet>{objectsXml}</Delete>");

            await SendDeleteBodyAsync(bucket, body, token).ConfigureAwait(false);
        }
    }

    /// In-progress multipart uploads — invisible in normal listings but they
    /// block bucket deletion until aborted.
    public async Task<List<MultipartUploadRef>> ListMultipartUploadsAsync(
        string bucket, CancellationToken token = default)
    {
        var all = new List<MultipartUploadRef>();
        string? keyMarker = null;
        string? uploadMarker = null;
        var more = true;

        while (more)
        {
            token.ThrowIfCancellationRequested();
            var query = new List<(string, string?)> { ("uploads", null), ("max-uploads", "1000") };
            if (keyMarker != null) query.Add(("key-marker", keyMarker));
            if (uploadMarker != null) query.Add(("upload-id-marker", uploadMarker));

            var (body, _) = await SendAsync(BuildRequest(HttpMethod.Get, bucket, query: query), token)
                .ConfigureAwait(false);
            var root = XmlTree.Parse(body);
            if (root == null) break;

            foreach (var node in root.All("Upload"))
            {
                var key = node.TextOf("Key");
                var uploadID = node.TextOf("UploadId");
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(uploadID)) continue;
                all.Add(new MultipartUploadRef(key!, uploadID!));
            }

            var truncated = root.TextOf("IsTruncated") == "true";
            keyMarker = root.TextOf("NextKeyMarker");
            uploadMarker = root.TextOf("NextUploadIdMarker");
            more = truncated && (keyMarker != null || uploadMarker != null);
        }
        return all;
    }

    public async Task<ObjectMetadata> HeadObjectAsync(
        string bucket, string key, CancellationToken token = default)
    {
        var prepared = BuildRequest(HttpMethod.Head, bucket, key);
        using var response = await Http
            .SendAsync(prepared.Message, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);

        if ((int)response.StatusCode is < 200 or >= 300)
        {
            throw new S3Exception((int)response.StatusCode, null, response.ReasonPhrase);
        }

        var meta = new ObjectMetadata
        {
            ContentType = response.Content.Headers.ContentType?.ToString(),
            ContentLength = response.Content.Headers.ContentLength,
            LastModified = response.Content.Headers.LastModified?.UtcDateTime
                .ToString("r", CultureInfo.InvariantCulture),
            ETag = response.Headers.ETag?.Tag.Replace("\"", "")
        };

        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            var lower = header.Key.ToLowerInvariant();
            var value = string.Join(",", header.Value);
            if (lower == "x-amz-storage-class") meta.StorageClass = value;
            if (lower.StartsWith("x-amz-meta-", StringComparison.Ordinal))
            {
                meta.Custom[lower["x-amz-meta-".Length..]] = value;
            }
            if (lower == "etag" && meta.ETag == null) meta.ETag = value.Replace("\"", "");
        }
        return meta;
    }

    /// Time-limited shareable URL for an object (SigV4 query-string auth).
    public string? PresignedUrl(string bucket, string key, TimeSpan expires)
    {
        var canonicalPath = CanonicalPath(bucket, key);
        return SigV4.Presign(
            Endpoint.Scheme,
            Endpoint.Host,
            Endpoint.IsDefaultPort ? (Endpoint.Scheme == "https" ? 443 : 80) : Endpoint.Port,
            canonicalPath,
            Credentials,
            Region,
            (int)expires.TotalSeconds);
    }

    /// True if an object with this exact key exists.
    public async Task<bool> ObjectExistsAsync(
        string bucket, string key, CancellationToken token = default)
    {
        try
        {
            await HeadObjectAsync(bucket, key, token).ConfigureAwait(false);
            return true;
        }
        catch (S3Exception error) when (error.Status == 404)
        {
            return false;
        }
    }

    // MARK: - Multipart upload

    public async Task<string> CreateMultipartUploadAsync(
        string bucket, string key, string? contentType = null, CancellationToken token = default)
    {
        var headers = new Dictionary<string, string>();
        if (contentType != null) headers["Content-Type"] = contentType;

        var prepared = BuildRequest(
            HttpMethod.Post, bucket, key,
            query: new List<(string, string?)> { ("uploads", null) },
            headers: headers);

        var (body, _) = await SendAsync(prepared, token).ConfigureAwait(false);
        var uploadID = XmlTree.Parse(body)?.TextOf("UploadId");
        if (string.IsNullOrEmpty(uploadID))
        {
            throw new S3Exception(200, "NoUploadId", "Missing UploadId in response");
        }
        return uploadID!;
    }

    /// Uploads one part; returns its ETag.
    public async Task<string> UploadPartAsync(
        string bucket,
        string key,
        string uploadID,
        int partNumber,
        byte[] data,
        Action<long, long>? progress = null,
        CancellationToken token = default)
    {
        HttpContent content = progress == null
            ? new ByteArrayContent(data)
            : new ProgressByteArrayContent(data, progress);

        var prepared = BuildRequest(
            HttpMethod.Put, bucket, key,
            query: new List<(string, string?)>
            {
                ("partNumber", partNumber.ToString(CultureInfo.InvariantCulture)),
                ("uploadId", uploadID)
            },
            content: content,
            payloadHash: SigV4.Sha256Hex(data));

        var (_, response) = await SendAsync(prepared, token).ConfigureAwait(false);
        var etag = response.Headers.ETag?.Tag
            ?? (response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null);
        if (etag == null)
        {
            throw new S3Exception((int)response.StatusCode, "NoETag", "Missing part ETag");
        }
        return etag.Replace("\"", "");
    }

    public async Task CompleteMultipartUploadAsync(
        string bucket,
        string key,
        string uploadID,
        IReadOnlyList<CompletedPart> parts,
        CancellationToken token = default)
    {
        var partsXml = string.Concat(parts
            .OrderBy(part => part.PartNumber)
            .Select(part =>
                $"<Part><PartNumber>{part.PartNumber}</PartNumber>" +
                $"<ETag>\"{S3Xml.Escape(part.ETag)}\"</ETag></Part>"));
        var body = Encoding.UTF8.GetBytes(
            $"<CompleteMultipartUpload>{partsXml}</CompleteMultipartUpload>");

        var prepared = BuildRequest(
            HttpMethod.Post, bucket, key,
            query: new List<(string, string?)> { ("uploadId", uploadID) },
            headers: new Dictionary<string, string> { ["Content-Type"] = "application/xml" },
            content: new ByteArrayContent(body),
            payloadHash: SigV4.Sha256Hex(body));

        var (responseBody, _) = await SendAsync(prepared, token).ConfigureAwait(false);
        // S3 can return 200 with an error body for CompleteMultipartUpload.
        var root = XmlTree.Parse(responseBody);
        if (root is { Name: "Error" })
        {
            throw new S3Exception(200, root.TextOf("Code"), root.TextOf("Message"));
        }
    }

    public async Task AbortMultipartUploadAsync(
        string bucket, string key, string uploadID, CancellationToken token = default) =>
        await SendAsync(
            BuildRequest(
                HttpMethod.Delete, bucket, key,
                query: new List<(string, string?)> { ("uploadId", uploadID) }),
            token)
        .ConfigureAwait(false);
}

/// Byte-array body that reports upload progress as it is written to the socket.
internal sealed class ProgressByteArrayContent : HttpContent
{
    private const int ChunkSize = 64 * 1024;

    private readonly byte[] _data;
    private readonly Action<long, long> _progress;

    public ProgressByteArrayContent(byte[] data, Action<long, long> progress)
    {
        _data = data;
        _progress = progress;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        long sent = 0;
        var total = (long)_data.Length;
        while (sent < total)
        {
            var length = (int)Math.Min(ChunkSize, total - sent);
            await stream.WriteAsync(_data.AsMemory((int)sent, length)).ConfigureAwait(false);
            sent += length;
            _progress(sent, total);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _data.Length;
        return true;
    }
}
