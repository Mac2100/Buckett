import Foundation

/// Tiny DOM-style wrapper around Foundation's XMLParser, sufficient for S3 responses.
final class XMLNode {
    let name: String
    var text: String = ""
    var children: [XMLNode] = []

    init(name: String) {
        self.name = name
    }

    /// First child with the given element name.
    subscript(_ childName: String) -> XMLNode? {
        children.first { $0.name == childName }
    }

    func all(_ childName: String) -> [XMLNode] {
        children.filter { $0.name == childName }
    }

    var trimmedText: String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

final class XMLTree: NSObject, XMLParserDelegate {
    private var stack: [XMLNode] = []
    private var root: XMLNode?

    static func parse(_ data: Data) -> XMLNode? {
        let tree = XMLTree()
        let parser = XMLParser(data: data)
        parser.delegate = tree
        parser.parse()
        return tree.root
    }

    func parser(
        _ parser: XMLParser,
        didStartElement elementName: String,
        namespaceURI: String?,
        qualifiedName qName: String?,
        attributes attributeDict: [String: String] = [:]
    ) {
        let node = XMLNode(name: elementName)
        if let parent = stack.last {
            parent.children.append(node)
        } else {
            root = node
        }
        stack.append(node)
    }

    func parser(_ parser: XMLParser, foundCharacters string: String) {
        stack.last?.text += string
    }

    func parser(
        _ parser: XMLParser,
        didEndElement elementName: String,
        namespaceURI: String?,
        qualifiedName qName: String?
    ) {
        if !stack.isEmpty { stack.removeLast() }
    }
}

enum S3Date {
    /// ISO 8601 with fractional seconds, as used in S3 XML (e.g. 2024-01-02T03:04:05.000Z).
    private static let fractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let plain: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()

    static func parse(_ string: String) -> Date? {
        fractional.date(from: string) ?? plain.date(from: string)
    }
}

/// Escapes text for embedding in request XML bodies (e.g. DeleteObjects keys).
func xmlEscape(_ s: String) -> String {
    s.replacingOccurrences(of: "&", with: "&amp;")
        .replacingOccurrences(of: "<", with: "&lt;")
        .replacingOccurrences(of: ">", with: "&gt;")
        .replacingOccurrences(of: "\"", with: "&quot;")
        .replacingOccurrences(of: "'", with: "&apos;")
}
