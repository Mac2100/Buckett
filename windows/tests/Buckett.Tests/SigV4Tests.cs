using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Buckett.Services;
using Xunit;

namespace Buckett.Tests;

/// The signer is the part of the port with no room for "looks right".
/// These tests check it two ways: the primitives against AWS's published
/// Signature Version 4 test vector, and full requests against an independent
/// reference signer written straight from the specification.
public class SigV4Tests
{
    private static readonly SigV4.Credentials Example = new(
        "AKIDEXAMPLE",
        "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY");

    private static readonly DateTime VectorDate =
        new(2015, 8, 30, 12, 36, 0, DateTimeKind.Utc);

    // MARK: - Published vector

    [Fact]
    public void ReproducesTheAwsGetVanillaVector()
    {
        // aws-sig-v4-test-suite / get-vanilla, signed with the reference chain
        // below. If the chain is right, it lands on AWS's published signature.
        var canonicalRequest = string.Join("\n",
            "GET",
            "/",
            "",
            "host:example.amazonaws.com\n" + "x-amz-date:20150830T123600Z\n",
            "host;x-amz-date",
            SigV4.EmptyPayloadHash);

        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            "20150830T123600Z",
            "20150830/us-east-1/service/aws4_request",
            SigV4.Sha256Hex(canonicalRequest));

        var signature = Reference.Sign(
            Example.SecretAccessKey, "20150830", "us-east-1", "service", stringToSign);

        Assert.Equal(
            "5fa00fa31553b73ebf1942676e86291e8372ff2a2260956d9b8aae1d763fbf31",
            signature);
    }

    [Fact]
    public void EmptyPayloadHashMatchesTheKnownSha256OfNothing() =>
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            SigV4.EmptyPayloadHash);

    // MARK: - Full requests vs. an independent implementation

    [Fact]
    public void SignsAPlainGetLikeTheReference() =>
        AssertMatchesReference(
            HttpMethod.Get,
            "https://account.r2.cloudflarestorage.com/photos",
            "/photos",
            "list-type=2&max-keys=1000",
            SigV4.EmptyPayloadHash,
            new Dictionary<string, string>());

    [Fact]
    public void SignsAPutWithContentTypeLikeTheReference() =>
        AssertMatchesReference(
            HttpMethod.Put,
            "https://account.r2.cloudflarestorage.com/photos/beach.jpg",
            "/photos/beach.jpg",
            "",
            SigV4.Sha256Hex("payload"),
            new Dictionary<string, string> { ["Content-Type"] = "image/jpeg" });

    [Fact]
    public void SignsACopyWithAmzHeadersLikeTheReference() =>
        AssertMatchesReference(
            HttpMethod.Put,
            "https://s3.us-west-004.backblazeb2.com/archive/new%20key.txt",
            "/archive/new%20key.txt",
            "",
            SigV4.EmptyPayloadHash,
            new Dictionary<string, string>
            {
                ["x-amz-copy-source"] = "/archive/old%20key.txt",
                ["x-amz-storage-class"] = "STANDARD"
            });

    [Fact]
    public void SignsANonDefaultPortWithTheHostHeaderIncludingIt() =>
        AssertMatchesReference(
            HttpMethod.Get,
            "https://minio.internal:9000/bucket/object",
            "/bucket/object",
            "",
            SigV4.EmptyPayloadHash,
            new Dictionary<string, string>());

    private static void AssertMatchesReference(
        HttpMethod method,
        string url,
        string canonicalPath,
        string canonicalQuery,
        string payloadHash,
        Dictionary<string, string> headers)
    {
        var uri = new Uri(url, new UriCreationOptions
        {
            DangerousDisablePathAndQueryCanonicalization = true
        });
        var request = new HttpRequestMessage(method, uri);
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                request.Content = new ByteArrayContent(Array.Empty<byte>());
                request.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(value);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        SigV4.Sign(
            request, canonicalPath, canonicalQuery, payloadHash,
            Example, "auto", "s3", VectorDate);

        var actual = Authorization(request);
        var expected = Reference.Authorization(
            method.Method, uri, canonicalPath, canonicalQuery, payloadHash,
            headers, Example, "auto", "s3", VectorDate);

        Assert.Equal(expected, actual);
    }

    private static string Authorization(HttpRequestMessage request) =>
        string.Join(",", request.Headers.GetValues("Authorization"));

    // MARK: - Percent-encoding

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "with%20space")]
    [InlineData("plus+sign", "plus%2Bsign")]
    [InlineData("tilde~dot.dash-underscore_", "tilde~dot.dash-underscore_")]
    [InlineData("héllo", "h%C3%A9llo")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("100%", "100%25")]
    [InlineData("amp&equals=", "amp%26equals%3D")]
    public void EncodesUnreservedCharactersPerRfc3986(string input, string expected) =>
        Assert.Equal(expected, SigV4.Encode(input));

    [Fact]
    public void KeepsSlashesWhenAskedTo() =>
        Assert.Equal(
            "folder/sub%20dir/file.txt",
            SigV4.Encode("folder/sub dir/file.txt", keepSlash: true));

    // MARK: - Presigned URLs

    [Fact]
    public void PresignedUrlCarriesEveryRequiredQueryParameter()
    {
        var url = SigV4.Presign(
            "https", "bucket.example.com", 443, "/bucket/key.txt",
            Example, "auto", expires: 3600, date: VectorDate);

        Assert.NotNull(url);
        Assert.StartsWith("https://bucket.example.com/bucket/key.txt?", url);
        Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", url);
        Assert.Contains("X-Amz-Credential=AKIDEXAMPLE%2F20150830%2Fauto%2Fs3%2Faws4_request", url);
        Assert.Contains("X-Amz-Date=20150830T123600Z", url);
        Assert.Contains("X-Amz-Expires=3600", url);
        Assert.Contains("X-Amz-SignedHeaders=host", url);
        Assert.Contains("X-Amz-Signature=", url);
    }

    [Fact]
    public void PresignedUrlMatchesTheReferenceSigner()
    {
        var url = SigV4.Presign(
            "https", "bucket.example.com", 443, "/bucket/key.txt",
            Example, "auto", expires: 900, date: VectorDate);

        var expected = Reference.PresignSignature(
            "GET", "bucket.example.com", "/bucket/key.txt", Example, "auto", "s3", 900, VectorDate);

        Assert.Contains("X-Amz-Signature=" + expected, url);
    }

    [Fact]
    public void PresignedUrlKeepsTheNonDefaultPortInTheHost()
    {
        var url = SigV4.Presign(
            "https", "minio.internal", 9000, "/bucket/key",
            Example, "auto", expires: 60, date: VectorDate);

        Assert.StartsWith("https://minio.internal:9000/bucket/key?", url);
    }
}

/// An independent Signature Version 4 implementation, written from the
/// specification rather than derived from the app's signer, so the two agreeing
/// is evidence rather than a tautology.
internal static class Reference
{
    public static string Sign(
        string secret, string dateStamp, string region, string service, string stringToSign)
    {
        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        key = Hmac(key, region);
        key = Hmac(key, service);
        key = Hmac(key, "aws4_request");
        return Hex(Hmac(key, stringToSign));
    }

    public static string Authorization(
        string method,
        Uri uri,
        string canonicalPath,
        string canonicalQuery,
        string payloadHash,
        IReadOnlyDictionary<string, string> extraHeaders,
        SigV4.Credentials credentials,
        string region,
        string service,
        DateTime date)
    {
        var amzDate = date.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        var dateStamp = amzDate[..8];

        var host = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };
        foreach (var (name, value) in extraHeaders)
        {
            headers[name.ToLowerInvariant()] = value.Trim();
        }

        var canonicalHeaders = string.Concat(headers.Select(pair => $"{pair.Key}:{pair.Value}\n"));
        var signedHeaders = string.Join(";", headers.Keys);

        var canonicalRequest = string.Join("\n",
            method,
            canonicalPath.Length == 0 ? "/" : canonicalPath,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadHash);

        var scope = $"{dateStamp}/{region}/{service}/aws4_request";
        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var signature = Sign(credentials.SecretAccessKey, dateStamp, region, service, stringToSign);
        return $"AWS4-HMAC-SHA256 Credential={credentials.AccessKeyID}/{scope}, " +
               $"SignedHeaders={signedHeaders}, Signature={signature}";
    }

    public static string PresignSignature(
        string method,
        string host,
        string canonicalPath,
        SigV4.Credentials credentials,
        string region,
        string service,
        int expires,
        DateTime date)
    {
        var amzDate = date.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        var dateStamp = amzDate[..8];
        var scope = $"{dateStamp}/{region}/{service}/aws4_request";

        var query = new List<(string Name, string Value)>
        {
            ("X-Amz-Algorithm", "AWS4-HMAC-SHA256"),
            ("X-Amz-Credential", $"{credentials.AccessKeyID}/{scope}"),
            ("X-Amz-Date", amzDate),
            ("X-Amz-Expires", expires.ToString()),
            ("X-Amz-SignedHeaders", "host")
        };
        var canonicalQuery = string.Join("&", query
            .Select(pair => (Name: SigV4.Encode(pair.Name), Value: SigV4.Encode(pair.Value)))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .Select(pair => $"{pair.Name}={pair.Value}"));

        var canonicalRequest = string.Join("\n",
            method, canonicalPath, canonicalQuery, $"host:{host}\n", "host", "UNSIGNED-PAYLOAD");

        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        return Sign(credentials.SecretAccessKey, dateStamp, region, service, stringToSign);
    }

    private static byte[] Hmac(byte[] key, string data) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
