using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Buckett.Services;

/// Tiny DOM-style wrapper around the XML reader, sufficient for S3 responses.
/// Element lookups ignore XML namespaces (S3 responses carry a default one).
public sealed class XmlNode
{
    public XmlNode(string name) => Name = name;

    public string Name { get; }
    public string Text { get; set; } = "";
    public List<XmlNode> Children { get; } = new();

    /// First child with the given element name.
    public XmlNode? this[string childName] =>
        Children.FirstOrDefault(child => child.Name == childName);

    public IReadOnlyList<XmlNode> All(string childName) =>
        Children.Where(child => child.Name == childName).ToList();

    public string TrimmedText => Text.Trim();

    /// Convenience for the very common "value of a child element, trimmed" lookup.
    public string? TextOf(string childName) => this[childName]?.TrimmedText;
}

public static class XmlTree
{
    public static XmlNode? Parse(byte[] data)
    {
        if (data.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(data);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };
            using var reader = XmlReader.Create(stream, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            return document.Root == null ? null : Convert(document.Root);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static XmlNode Convert(XElement element)
    {
        var node = new XmlNode(element.Name.LocalName);
        foreach (var child in element.Elements())
        {
            node.Children.Add(Convert(child));
        }
        if (!element.HasElements)
        {
            node.Text = element.Value;
        }
        return node;
    }
}

public static class S3Date
{
    /// ISO 8601 with or without fractional seconds, as used in S3 XML
    /// (e.g. 2024-01-02T03:04:05.000Z).
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }
        return null;
    }
}

public static class S3Xml
{
    /// Escapes text for embedding in request XML bodies (e.g. DeleteObjects keys).
    public static string Escape(string value) =>
        value.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;")
             .Replace("'", "&apos;");
}
