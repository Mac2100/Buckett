import SwiftUI
import Quartz

// MARK: - Quick Look wrapper

struct QuickLookPreview: NSViewRepresentable {
    let url: URL

    func makeNSView(context: Context) -> QLPreviewView {
        let view = QLPreviewView(frame: .zero, style: .normal) ?? QLPreviewView()
        view.shouldCloseWithWindow = false
        view.previewItem = url as NSURL
        return view
    }

    func updateNSView(_ nsView: QLPreviewView, context: Context) {
        if nsView.previewItem?.previewItemURL != url {
            nsView.previewItem = url as NSURL
        }
    }
}

// MARK: - Preview sheet

struct PreviewSheet: View {
    let object: RemoteObject
    let bucket: String
    let client: S3Client

    @Environment(\.dismiss) private var dismiss
    @State private var localURL: URL?
    @State private var error: String?

    /// Objects above this size are not auto-downloaded for preview.
    private static let maxPreviewBytes: Int64 = 256 * 1024 * 1024

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Image(systemName: object.symbolName)
                VStack(alignment: .leading, spacing: 1) {
                    Text(object.name).font(.headline)
                    Text("\(object.formattedSize)  ·  \(object.key)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .truncationMode(.middle)
                        .lineLimit(1)
                }
                Spacer()
                if let localURL {
                    Button("Open") { NSWorkspace.shared.open(localURL) }
                }
                Button("Done") { dismiss() }
                    .keyboardShortcut(.cancelAction)
            }
            .padding(12)
            Divider()
            contentArea
        }
        .frame(width: 760, height: 560)
        .task { await fetch() }
    }

    @ViewBuilder
    private var contentArea: some View {
        if let error {
            VStack(spacing: 8) {
                Image(systemName: "exclamationmark.triangle")
                    .font(.largeTitle)
                    .foregroundStyle(.orange)
                Text(error).foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let localURL {
            QuickLookPreview(url: localURL)
        } else {
            VStack(spacing: 10) {
                ProgressView()
                Text("Fetching \(object.name)…").foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func fetch() async {
        guard object.size <= Self.maxPreviewBytes else {
            error = "This file is too large to preview. Use Download instead."
            return
        }
        do {
            let directory = FileManager.default.temporaryDirectory
                .appendingPathComponent("BuckettPreviews", isDirectory: true)
                .appendingPathComponent(UUID().uuidString, isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let destination = directory.appendingPathComponent(object.name)
            try await client.downloadObject(bucket: bucket, key: object.key, to: destination)
            localURL = destination
        } catch {
            self.error = error.localizedDescription
        }
    }
}

// MARK: - Metadata sheet

struct MetadataSheet: View {
    let object: RemoteObject
    @ObservedObject var model: BrowserModel

    @Environment(\.dismiss) private var dismiss
    @State private var metadata: ObjectMetadata?
    @State private var loading = true

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Info").font(.headline)
                Spacer()
                Button("Done") { dismiss() }
                    .keyboardShortcut(.cancelAction)
            }
            .padding(12)
            Divider()

            if loading {
                ProgressView()
                    .frame(maxWidth: .infinity, minHeight: 160)
            } else {
                ScrollView {
                    Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 16, verticalSpacing: 8) {
                        row("Name", object.name)
                        row("Key", object.key)
                        row("Size", metadata?.contentLength.map { $0.formattedBytes } ?? object.formattedSize)
                        row("Content Type", metadata?.contentType ?? "—")
                        row("Last Modified", metadata?.lastModified
                            ?? object.lastModified?.formatted() ?? "—")
                        row("ETag", metadata?.eTag ?? object.eTag ?? "—")
                        row("Storage Class", metadata?.storageClass ?? object.storageClass ?? "Standard")

                        if let custom = metadata?.custom, !custom.isEmpty {
                            GridRow {
                                Text("Custom Metadata")
                                    .foregroundStyle(.secondary)
                                    .gridColumnAlignment(.trailing)
                                VStack(alignment: .leading, spacing: 3) {
                                    ForEach(custom.sorted(by: { $0.key < $1.key }), id: \.key) { entry in
                                        Text("\(entry.key): \(entry.value)")
                                            .font(.callout.monospaced())
                                    }
                                }
                            }
                        }
                    }
                    .padding(16)
                    .textSelection(.enabled)
                }
            }
        }
        .frame(width: 480, height: 360)
        .task {
            metadata = await model.metadata(for: object)
            loading = false
        }
    }

    private func row(_ label: String, _ value: String) -> some View {
        GridRow {
            Text(label)
                .foregroundStyle(.secondary)
                .gridColumnAlignment(.trailing)
            Text(value)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

// MARK: - Rename sheet

struct RenameSheet: View {
    let object: RemoteObject
    @ObservedObject var model: BrowserModel

    @Environment(\.dismiss) private var dismiss
    @State private var name = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Rename \(object.isFolder ? "Folder" : "File")").font(.headline)
            TextField("Name", text: $name)
                .textFieldStyle(.roundedBorder)
                .frame(width: 300)
            if object.isFolder {
                Text("Renaming a folder copies every object under it to the new prefix.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Rename") {
                    let newName = name
                    dismiss()
                    Task { await model.rename(object: object, to: newName) }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(
                    name.trimmingCharacters(in: .whitespaces).isEmpty
                    || name == object.name
                    || name.contains("/")
                )
            }
        }
        .padding(20)
        .onAppear { name = object.name }
    }
}

// MARK: - Batch rename sheet

struct BatchRenameSheet: View {
    @ObservedObject var model: BrowserModel

    @Environment(\.dismiss) private var dismiss
    @State private var find = ""
    @State private var replace = ""

    private var affected: [(RemoteObject, String)] {
        guard !find.isEmpty else { return [] }
        return model.selectedObjects
            .filter { !$0.isFolder }
            .compactMap { object in
                let newName = object.name.replacingOccurrences(of: find, with: replace)
                guard newName != object.name, !newName.isEmpty, !newName.contains("/") else {
                    return nil
                }
                return (object, newName)
            }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Batch Rename").font(.headline)
            Text("Find and replace within the names of the selected files.")
                .font(.caption)
                .foregroundStyle(.secondary)

            Form {
                TextField("Find", text: $find)
                TextField("Replace with", text: $replace)
            }
            .frame(width: 320)

            if !affected.isEmpty {
                Text("Preview")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
                ScrollView {
                    VStack(alignment: .leading, spacing: 3) {
                        ForEach(affected.prefix(20), id: \.0.id) { pair in
                            Text("\(pair.0.name)  →  \(pair.1)")
                                .font(.caption.monospaced())
                                .lineLimit(1)
                                .truncationMode(.middle)
                        }
                        if affected.count > 20 {
                            Text("…and \(affected.count - 20) more")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                .frame(maxHeight: 140)
            }

            HStack {
                Spacer()
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Rename \(affected.count) File\(affected.count == 1 ? "" : "s")") {
                    let f = find, r = replace
                    dismiss()
                    Task { await model.batchRename(find: f, replace: r) }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(affected.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 400)
    }
}
