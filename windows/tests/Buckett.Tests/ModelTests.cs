using System;
using System.Collections.Generic;
using System.Linq;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;
using Xunit;

namespace Buckett.Tests;

public class AccountTests
{
    [Fact]
    public void DerivesTheR2EndpointFromTheAccountId()
    {
        var account = new Account
        {
            Provider = Provider.CloudflareR2,
            CloudflareAccountID = " abc123 "
        };
        Assert.Equal("https://abc123.r2.cloudflarestorage.com/", account.EndpointUrl?.ToString());
        Assert.Equal("auto", account.SigningRegion);
    }

    [Fact]
    public void DerivesTheB2EndpointFromTheRegion()
    {
        var account = new Account { Provider = Provider.BackblazeB2, B2Region = "us-west-004" };
        Assert.Equal("https://s3.us-west-004.backblazeb2.com/", account.EndpointUrl?.ToString());
        Assert.Equal("us-west-004", account.SigningRegion);
    }

    [Fact]
    public void FallsBackToUsEast1ForAmazonS3()
    {
        var account = new Account { Provider = Provider.AmazonS3 };
        Assert.Equal("https://s3.us-east-1.amazonaws.com/", account.EndpointUrl?.ToString());
        Assert.Equal("us-east-1", account.SigningRegion);
    }

    [Fact]
    public void CustomEndpointWinsAndGainsAScheme()
    {
        var account = new Account
        {
            Provider = Provider.CloudflareR2,
            CloudflareAccountID = "abc123",
            CustomEndpoint = "minio.internal:9000/"
        };
        Assert.Equal("https://minio.internal:9000/", account.EndpointUrl?.ToString());
    }

    [Fact]
    public void IsNotConfiguredWithoutAnEndpointOrKey()
    {
        Assert.False(new Account().IsConfigured);
        Assert.False(new Account { CloudflareAccountID = "abc" }.IsConfigured);
        Assert.True(new Account { CloudflareAccountID = "abc", AccessKeyID = "AK" }.IsConfigured);
    }

    [Fact]
    public void ProviderRawValuesMatchTheMacOsFileFormat()
    {
        Assert.Equal("r2", Provider.CloudflareR2.RawValue());
        Assert.Equal("b2", Provider.BackblazeB2.RawValue());
        Assert.Equal("s3", Provider.AmazonS3.RawValue());
        Assert.Equal(Provider.BackblazeB2, ProviderExtensions.FromRawValue("b2"));
        Assert.Equal(Provider.CloudflareR2, ProviderExtensions.FromRawValue("nonsense"));
    }
}

public class RemoteObjectTests
{
    [Theory]
    [InlineData("photos/2024/beach.jpg", false, "beach.jpg", "jpg")]
    [InlineData("beach.JPG", false, "beach.JPG", "jpg")]
    [InlineData("noextension", false, "noextension", "")]
    [InlineData("photos/2024/", true, "2024", "")]
    public void SplitsKeysIntoNameAndExtension(
        string key, bool isFolder, string name, string extension)
    {
        var remote = new RemoteObject { Key = key, IsFolder = isFolder };
        Assert.Equal(name, remote.Name);
        Assert.Equal(extension, remote.FileExtension);
    }

    [Fact]
    public void ClassifiesFilesByExtension()
    {
        Assert.True(new RemoteObject { Key = "a.png" }.IsImage);
        Assert.True(new RemoteObject { Key = "a.mp4" }.IsVideo);
        Assert.True(new RemoteObject { Key = "a.flac" }.IsAudio);
        Assert.True(new RemoteObject { Key = "a.cs" }.IsText);
        Assert.False(new RemoteObject { Key = "a.bin" }.IsImage);
    }

    [Fact]
    public void PicksSymbolsThatMatchTheMacOsBuild()
    {
        Assert.Equal("folder.fill", new RemoteObject { Key = "x/", IsFolder = true }.SymbolName);
        Assert.Equal("photo", new RemoteObject { Key = "x.jpg" }.SymbolName);
        Assert.Equal("film", new RemoteObject { Key = "x.mov" }.SymbolName);
        Assert.Equal("waveform", new RemoteObject { Key = "x.mp3" }.SymbolName);
        Assert.Equal("doc.richtext", new RemoteObject { Key = "x.pdf" }.SymbolName);
        Assert.Equal("doc.zipper", new RemoteObject { Key = "x.zip" }.SymbolName);
        Assert.Equal(
            "chevron.left.forwardslash.chevron.right",
            new RemoteObject { Key = "x.json" }.SymbolName);
        Assert.Equal("doc.text", new RemoteObject { Key = "x.md" }.SymbolName);
        Assert.Equal("doc", new RemoteObject { Key = "x.bin" }.SymbolName);
    }

    [Fact]
    public void FoldersHaveNoSize() =>
        Assert.Equal("—", new RemoteObject { Key = "x/", IsFolder = true }.FormattedSize);
}

public class ByteFormatTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(999, "999 bytes")]
    [InlineData(1000, "1.0 KB")]
    [InlineData(1_500_000, "1.5 MB")]
    [InlineData(1_073_741_824, "1.1 GB")]
    [InlineData(1_000_000_000_000, "1.0 TB")]
    public void FormatsLikeMacOsByteCountFormatter(long bytes, string expected) =>
        Assert.Equal(expected, ByteFormat.String(bytes));
}

public class NaturalComparerTests
{
    [Fact]
    public void OrdersDigitRunsNumerically()
    {
        var names = new List<string> { "file10.txt", "file2.txt", "File1.txt", "file20.txt" };
        names.Sort(NaturalComparer.Instance);
        Assert.Equal(
            new[] { "File1.txt", "file2.txt", "file10.txt", "file20.txt" },
            names);
    }

    [Fact]
    public void IgnoresLeadingZerosInsideNumbers()
    {
        var names = new List<string> { "img007", "img8", "img06" };
        names.Sort(NaturalComparer.Instance);
        Assert.Equal(new[] { "img06", "img007", "img8" }, names);
    }
}

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.7.5", "1.7.4", true)]
    [InlineData("1.7.10", "1.7.9", true)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("1.7.4", "1.7.4", false)]
    [InlineData("1.7.3", "1.7.4", false)]
    [InlineData("1.8", "1.7.9", true)]
    public void ComparesDottedVersionsNumerically(string candidate, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsVersionNewer(candidate, current));
}

public class DropTargetTests
{
    [Fact]
    public void RoundTripsThroughItsEncodedForm()
    {
        var target = new DropTarget(Guid.NewGuid(), "my-bucket");
        var decoded = DropTarget.Decode(target.Encoded);
        Assert.NotNull(decoded);
        Assert.Equal(target.AccountID, decoded!.Value.AccountID);
        Assert.Equal(target.Bucket, decoded.Value.Bucket);
    }

    [Fact]
    public void KeepsPipesThatAppearInsideBucketNames()
    {
        var target = new DropTarget(Guid.NewGuid(), "odd|name");
        Assert.Equal("odd|name", DropTarget.Decode(target.Encoded)!.Value.Bucket);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid|bucket")]
    [InlineData("|bucket")]
    public void RejectsMalformedInput(string encoded) => Assert.Null(DropTarget.Decode(encoded));

    [Fact]
    public void RejectsAnEmptyBucketName() =>
        Assert.Null(DropTarget.Decode(Guid.NewGuid().ToString("D") + "|"));
}

public class StatsTests
{
    [Fact]
    public void SummarisesObjectsByExtensionLargestFirst()
    {
        var objects = new List<RemoteObject>
        {
            new() { Key = "a.jpg", Size = 100 },
            new() { Key = "b.jpg", Size = 300 },
            new() { Key = "c.png", Size = 50 },
            new() { Key = "d", Size = 25 },
            new() { Key = "folder/", IsFolder = true }
        };

        var stats = AppState.ComputeStats("photos", objects);

        Assert.Equal(4, stats.ObjectCount);
        Assert.Equal(475, stats.TotalSize);
        Assert.Equal("jpg", stats.ByExtension[0].Ext);
        Assert.Equal(400, stats.ByExtension[0].TotalSize);
        Assert.Equal(2, stats.ByExtension[0].Count);
        Assert.Contains(stats.ByExtension, item => item.Ext == "(none)");
        Assert.Equal("b.jpg", stats.LargestObjects[0].Name);
    }

    [Fact]
    public void ReportsNoNewestDateWhenNothingIsDated()
    {
        var stats = AppState.ComputeStats(
            "empty", new List<RemoteObject> { new() { Key = "a", Size = 1 } });
        Assert.Null(stats.NewestModified);
    }
}

public class XmlTests
{
    [Fact]
    public void ParsesNamespacedListBucketsResponses()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ListAllMyBucketsResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
              <Buckets>
                <Bucket><Name>photos</Name><CreationDate>2024-01-02T03:04:05.000Z</CreationDate></Bucket>
                <Bucket><Name>backups</Name><CreationDate>2024-05-06T07:08:09Z</CreationDate></Bucket>
              </Buckets>
            </ListAllMyBucketsResult>
            """;

        var root = XmlTree.Parse(System.Text.Encoding.UTF8.GetBytes(xml));

        Assert.NotNull(root);
        var buckets = root!["Buckets"]!.All("Bucket");
        Assert.Equal(2, buckets.Count);
        Assert.Equal("photos", buckets[0].TextOf("Name"));
        Assert.Equal(
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            S3Date.Parse(buckets[0].TextOf("CreationDate")));
        Assert.Equal(
            new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
            S3Date.Parse(buckets[1].TextOf("CreationDate")));
    }

    [Fact]
    public void ReturnsNullForRubbish() =>
        Assert.Null(XmlTree.Parse(System.Text.Encoding.UTF8.GetBytes("not xml at all")));

    [Fact]
    public void EscapesKeysForDeleteBodies() =>
        Assert.Equal(
            "a&amp;b&lt;c&gt;d&quot;e&apos;f",
            S3Xml.Escape("a&b<c>d\"e'f"));
}

public class ConflictStrategyTests
{
    [Fact]
    public void ExposesTheSameThreeStrategiesAsMacOs()
    {
        Assert.Equal(3, ConflictStrategyExtensions.All.Count);
        Assert.Equal("Skip", ConflictStrategy.Skip.Label());
        Assert.Equal("Replace", ConflictStrategy.Replace.Label());
        Assert.Equal("Rename", ConflictStrategy.Rename.Label());
        Assert.All(
            ConflictStrategyExtensions.All,
            strategy => Assert.False(string.IsNullOrWhiteSpace(strategy.Detail())));
    }
}

public class IconTests
{
    /// Every symbol the views ask for has to exist, or a control renders a
    /// fallback document glyph where a meaningful icon belongs.
    [Fact]
    public void AllReferencedSymbolsAreDefined()
    {
        string[] used =
        {
            "archivebox.fill", "basket.fill", "tray.full.fill", "cloud.fill", "externaldrive.fill",
            "folder", "folder.fill", "folder.badge.plus", "photo", "film", "waveform",
            "doc", "doc.text", "doc.richtext", "doc.zipper", "doc.on.doc", "doc.on.doc.fill",
            "chevron.left.forwardslash.chevron.right", "square.grid.2x2", "square.grid.2x2.fill",
            "list.bullet", "magnifyingglass", "magnifyingglass.circle.fill", "xmark",
            "xmark.circle.fill", "checkmark", "checkmark.circle.fill", "checkmark.seal",
            "chevron.up", "chevron.right", "chevron.up.chevron.down", "plus", "minus",
            "arrow.clockwise", "arrow.clockwise.circle.fill", "arrow.triangle.2.circlepath",
            "arrow.up.arrow.down", "arrow.up.arrow.down.circle.fill", "arrow.up.circle",
            "arrow.down.circle", "arrow.down.circle.fill", "arrow.up.doc", "arrow.up.right.square",
            "arrow.down.and.line.horizontal.and.arrow.up", "square.and.arrow.up",
            "square.and.arrow.down", "tray.full", "tray.2.fill", "internaldrive",
            "internaldrive.fill", "trash", "trash.circle.fill", "pencil", "link",
            "link.circle.fill", "tag.fill", "gearshape", "key", "key.slash", "lock.shield",
            "lock.shield.fill", "person.crop.circle", "person.crop.circle.badge.plus",
            "person.crop.circle.badge.questionmark", "square.stack.3d.up", "chart.bar",
            "chart.bar.xaxis", "chart.bar.doc.horizontal", "clock.fill", "dollarsign.circle.fill",
            "info.circle", "info.circle.fill", "exclamationmark.triangle",
            "exclamationmark.triangle.fill", "bell.badge", "paintpalette", "ellipsis.circle",
            "star.fill", "cup.and.saucer.fill", "cloud", "eye.slash", "shippingbox.fill",
            "flame.fill"
        };

        var missing = used.Where(symbol => !Views.Icons.Contains(symbol)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryIconHasGeometryToDraw() =>
        Assert.All(Views.Icons.All.Values, icon =>
            Assert.True(
                !string.IsNullOrWhiteSpace(icon.Stroke) || !string.IsNullOrWhiteSpace(icon.Fill)));
}
