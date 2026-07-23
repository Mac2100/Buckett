import SwiftUI
import UniformTypeIdentifiers

struct BrowserView: View {
    @StateObject private var model: BrowserModel

    @State private var previewObject: RemoteObject?
    @State private var metadataObject: RemoteObject?
    @State private var renameObject: RemoteObject?
    @State private var showBatchRename = false
    @State private var deleteTargets: [RemoteObject] = []
    @State private var showDeleteConfirm = false
    @State private var showNewFolder = false
    @State private var newFolderName = ""
    @State private var isDropTargeted = false

    init(bucket: String, client: S3Client, transfers: TransferManager) {
        _model = StateObject(
            wrappedValue: BrowserModel(bucket: bucket, client: client, transfers: transfers)
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            breadcrumbBar
            Divider()
            content
            Divider()
            statusBar
        }
        .searchable(text: $model.filterText, placement: .toolbar, prompt: "Filter by name")
        .toolbar { toolbarContent }
        .onDrop(of: [.fileURL], isTargeted: $isDropTargeted) { providers in
            FileDrop.loadFileURLs(from: providers) { urls in
                guard !urls.isEmpty else { return }
                model.upload(urls: urls)
            }
            return true
        }
        .overlay {
            if isDropTargeted {
                dropOverlay
            }
        }
        .task { await model.load() }
        .sheet(item: $previewObject) { object in
            PreviewSheet(object: object, bucket: model.bucket, client: model.client)
        }
        .sheet(item: $metadataObject) { object in
            MetadataSheet(object: object, model: model)
        }
        .sheet(item: $renameObject) { object in
            RenameSheet(object: object, model: model)
        }
        .sheet(isPresented: $showBatchRename) {
            BatchRenameSheet(model: model)
        }
        .sheet(isPresented: $showNewFolder) { newFolderSheet }
        .confirmationDialog(
            deleteMessage,
            isPresented: $showDeleteConfirm,
            titleVisibility: .visible
        ) {
            Button("Delete", role: .destructive) {
                let targets = deleteTargets
                Task { await model.delete(objects: targets) }
            }
            Button("Cancel", role: .cancel) {}
        }
        .alert(
            "Error",
            isPresented: Binding(
                get: { model.errorMessage != nil },
                set: { if !$0 { model.errorMessage = nil } }
            )
        ) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(model.errorMessage ?? "")
        }
        .navigationTitle(model.bucket)
    }

    private var deleteMessage: String {
        deleteTargets.count == 1
            ? "Delete “\(deleteTargets[0].name)”? This cannot be undone."
            : "Delete \(deleteTargets.count) items? This cannot be undone."
    }

    // MARK: - Content

    @ViewBuilder
    private var content: some View {
        if model.isLoading && model.displayItems.isEmpty {
            Spacer()
            ProgressView("Loading…")
            Spacer()
        } else if model.displayItems.isEmpty {
            emptyState
        } else {
            switch model.viewMode {
            case .grid:
                ObjectGridView(
                    model: model,
                    onOpen: handleOpen,
                    onContextAction: handleAction
                )
            case .list:
                ObjectListView(
                    model: model,
                    onOpen: handleOpen,
                    onContextAction: handleAction
                )
            }
        }
    }

    private var emptyState: some View {
        VStack(spacing: 10) {
            Spacer()
            Image(systemName: "tray")
                .font(.system(size: 42))
                .foregroundStyle(.tertiary)
            Text(model.filterText.isEmpty ? "This folder is empty" : "No matches")
                .foregroundStyle(.secondary)
            if model.filterText.isEmpty {
                Text("Drop files here to upload")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }

    private var dropOverlay: some View {
        RoundedRectangle(cornerRadius: 10)
            .strokeBorder(Color.accentColor, style: StrokeStyle(lineWidth: 3, dash: [8]))
            .background(Color.accentColor.opacity(0.08), in: RoundedRectangle(cornerRadius: 10))
            .overlay {
                Label("Drop to upload to \(model.bucket)/\(model.prefix)", systemImage: "arrow.up.doc")
                    .font(.title3.weight(.medium))
                    .padding(12)
                    .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 8))
            }
            .padding(8)
            .allowsHitTesting(false)
    }

    // MARK: - Breadcrumbs

    private var breadcrumbBar: some View {
        HStack(spacing: 4) {
            Button {
                model.navigateUp()
            } label: {
                Image(systemName: "chevron.up")
            }
            .buttonStyle(.borderless)
            .disabled(model.prefix.isEmpty)
            .help("Up one level")

            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 2) {
                    ForEach(Array(model.breadcrumbs.enumerated()), id: \.offset) { index, crumb in
                        if index > 0 {
                            Image(systemName: "chevron.right")
                                .font(.caption2)
                                .foregroundStyle(.tertiary)
                        }
                        Button {
                            model.navigate(toPrefix: crumb.prefix)
                        } label: {
                            HStack(spacing: 3) {
                                if index == 0 {
                                    Image(systemName: "tray.full")
                                }
                                Text(crumb.name)
                            }
                        }
                        .buttonStyle(.borderless)
                        .fontWeight(index == model.breadcrumbs.count - 1 ? .semibold : .regular)
                    }
                }
            }
            Spacer()
            if model.isBusy || model.isLoading {
                ProgressView().controlSize(.small)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
    }

    // MARK: - Status bar

    private var statusBar: some View {
        HStack(spacing: 12) {
            Text("\(model.folders.count) folders, \(model.files.count) files")
                .foregroundStyle(.secondary)
            if !model.selection.isEmpty {
                Text("\(model.selection.count) selected")
                    .foregroundStyle(.secondary)
                let selectedSize = model.selectedObjects
                    .filter { !$0.isFolder }
                    .reduce(Int64(0)) { $0 + $1.size }
                if selectedSize > 0 {
                    Text(selectedSize.formattedBytes)
                        .foregroundStyle(.secondary)
                }
            }
            if model.isTruncated {
                Button("Load more…") {
                    Task { await model.loadMore() }
                }
                .buttonStyle(.link)
            }
            Spacer()
            TransfersStatusLabel(transfers: model.transfers)
        }
        .font(.caption)
        .padding(.horizontal, 12)
        .padding(.vertical, 5)
    }

    // MARK: - Toolbar

    @ToolbarContentBuilder
    private var toolbarContent: some ToolbarContent {
        ToolbarItemGroup {
            Picker("View", selection: $model.viewMode) {
                ForEach(ViewMode.allCases) { mode in
                    Image(systemName: mode.symbolName)
                        .help(mode.label)
                        .tag(mode)
                }
            }
            .pickerStyle(.segmented)

            Menu {
                Picker("Sort by", selection: $model.sortField) {
                    ForEach(SortField.allCases) { field in
                        Text(field.label).tag(field)
                    }
                }
                Divider()
                Picker("Order", selection: $model.sortAscending) {
                    Text("Ascending").tag(true)
                    Text("Descending").tag(false)
                }
            } label: {
                Label("Sort", systemImage: "arrow.up.arrow.down")
            }
            .help("Sort")

            Button {
                let urls = Panels.chooseFilesForUpload()
                guard !urls.isEmpty else { return }
                model.upload(urls: urls)
            } label: {
                Label("Upload", systemImage: "square.and.arrow.up")
            }
            .help("Upload files or folders")

            Button {
                downloadSelection()
            } label: {
                Label("Download", systemImage: "square.and.arrow.down")
            }
            .help("Download selection")
            .disabled(model.selection.isEmpty)

            Button {
                requestDelete(model.selectedObjects)
            } label: {
                Label("Delete", systemImage: "trash")
            }
            .help("Delete selection")
            .disabled(model.selection.isEmpty)

            Menu {
                Button("New Folder…") {
                    newFolderName = ""
                    showNewFolder = true
                }
                Button("Batch Rename…") {
                    showBatchRename = true
                }
                .disabled(model.selectedObjects.filter { !$0.isFolder }.isEmpty)
                Button("Copy Keys") {
                    copyKeys(model.selectedObjects)
                }
                .disabled(model.selection.isEmpty)
            } label: {
                Label("More", systemImage: "ellipsis.circle")
            }

            TransfersToolbarButton(transfers: model.transfers)

            Button {
                Task { await model.refresh() }
            } label: {
                Label("Refresh", systemImage: "arrow.clockwise")
            }
            .help("Refresh")
        }
    }

    // MARK: - Actions

    private func handleOpen(_ object: RemoteObject) {
        if object.isFolder {
            model.open(folder: object)
        } else {
            previewObject = object
        }
    }

    private func handleAction(_ action: ObjectAction, _ objects: [RemoteObject]) {
        switch action {
        case .preview:
            if let first = objects.first, !first.isFolder {
                previewObject = first
            }
        case .download:
            guard let directory = Panels.chooseDownloadDirectory() else { return }
            model.download(objects: objects, to: directory)
        case .rename:
            renameObject = objects.first
        case .delete:
            requestDelete(objects)
        case .copyKey:
            copyKeys(objects)
        case .metadata:
            if let first = objects.first, !first.isFolder {
                metadataObject = first
            }
        }
    }

    private func downloadSelection() {
        let objects = model.selectedObjects
        guard !objects.isEmpty, let directory = Panels.chooseDownloadDirectory() else { return }
        model.download(objects: objects, to: directory)
    }

    private func requestDelete(_ objects: [RemoteObject]) {
        guard !objects.isEmpty else { return }
        deleteTargets = objects
        showDeleteConfirm = true
    }

    private func copyKeys(_ objects: [RemoteObject]) {
        let keys = objects.map(\.key).joined(separator: "\n")
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(keys, forType: .string)
    }

    private var newFolderSheet: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("New Folder").font(.headline)
            TextField("Folder name", text: $newFolderName)
                .textFieldStyle(.roundedBorder)
                .frame(width: 260)
            HStack {
                Spacer()
                Button("Cancel") { showNewFolder = false }
                    .keyboardShortcut(.cancelAction)
                Button("Create") {
                    let name = newFolderName
                    showNewFolder = false
                    Task { await model.createFolder(named: name) }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(newFolderName.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
    }
}

// MARK: - Shared bits

enum ObjectAction {
    case preview, download, rename, delete, copyKey, metadata
}

/// Small label in the status bar summarizing transfer activity.
struct TransfersStatusLabel: View {
    @ObservedObject var transfers: TransferManager

    var body: some View {
        if transfers.activeCount > 0 {
            HStack(spacing: 5) {
                ProgressView().controlSize(.mini)
                Text("\(transfers.activeCount) transfer\(transfers.activeCount == 1 ? "" : "s") active")
                    .foregroundStyle(.secondary)
            }
        }
    }
}

struct TransfersToolbarButton: View {
    @ObservedObject var transfers: TransferManager
    @State private var showPopover = false

    var body: some View {
        Button {
            showPopover.toggle()
        } label: {
            Label("Transfers", systemImage: "arrow.up.arrow.down.circle")
        }
        .help("Show transfers")
        .overlay(alignment: .topTrailing) {
            if transfers.activeCount > 0 {
                Text("\(transfers.activeCount)")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 4)
                    .padding(.vertical, 1)
                    .background(Capsule().fill(Color.accentColor))
                    .offset(x: 6, y: -4)
            }
        }
        .popover(isPresented: $showPopover, arrowEdge: .bottom) {
            TransfersView(transfers: transfers)
        }
    }
}

// MARK: - Panels & drop helpers

@MainActor
enum Panels {
    static func chooseFilesForUpload() -> [URL] {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        panel.prompt = "Upload"
        panel.message = "Choose files or folders to upload"
        return panel.runModal() == .OK ? panel.urls : []
    }

    static func chooseDownloadDirectory() -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        panel.prompt = "Download Here"
        panel.message = "Choose a destination folder"
        return panel.runModal() == .OK ? panel.url : nil
    }
}

enum FileDrop {
    /// Extracts file URLs from drag-and-drop item providers, then calls back on the main actor.
    static func loadFileURLs(
        from providers: [NSItemProvider],
        completion: @escaping @MainActor ([URL]) -> Void
    ) {
        let group = DispatchGroup()
        let lock = NSLock()
        var urls: [URL] = []

        for provider in providers
        where provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
            group.enter()
            provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, _ in
                defer { group.leave() }
                var url: URL?
                if let data = item as? Data {
                    url = URL(dataRepresentation: data, relativeTo: nil)
                } else if let itemURL = item as? URL {
                    url = itemURL
                }
                if let url {
                    lock.lock()
                    urls.append(url)
                    lock.unlock()
                }
            }
        }
        group.notify(queue: .main) {
            let collected = urls
            Task { @MainActor in
                completion(collected)
            }
        }
    }
}
