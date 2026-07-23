import SwiftUI
import UniformTypeIdentifiers

struct BrowserView: View {
    @StateObject private var model: BrowserModel
    @Environment(\.appTheme) private var theme

    @State private var previewObject: RemoteObject?
    @State private var metadataObject: RemoteObject?
    @State private var renameObject: RemoteObject?
    @State private var showBatchRename = false
    @State private var showMove = false
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
            controlsBar
            Divider().opacity(0.4)
            ZStack(alignment: .bottom) {
                content
                if !model.selection.isEmpty {
                    selectionBar
                        .padding(.bottom, 14)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .animation(.snappy(duration: 0.2), value: model.selection.isEmpty)
            Divider().opacity(0.4)
            statusBar
        }
        .onDrop(of: [.fileURL], isTargeted: $isDropTargeted) { providers in
            FileDrop.loadFileURLs(from: providers) { urls in
                guard !urls.isEmpty else { return }
                model.upload(urls: urls)
                ToastCenter.shared.show(
                    "Uploading \(urls.count) item\(urls.count == 1 ? "" : "s")",
                    style: .info
                )
            }
            return true
        }
        .overlay {
            if isDropTargeted { dropOverlay }
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
        .sheet(isPresented: $showMove) {
            MoveSheet(model: model)
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
    }

    private var deleteMessage: String {
        deleteTargets.count == 1
            ? "Delete “\(deleteTargets[0].name)”? This cannot be undone."
            : "Delete \(deleteTargets.count) items? This cannot be undone."
    }

    // MARK: - Controls bar

    private var controlsBar: some View {
        HStack(spacing: 10) {
            breadcrumb
            Spacer(minLength: 12)
            SearchField(text: $model.filterText, prompt: "Filter by name")

            Button {
                let urls = Panels.chooseFilesForUpload()
                guard !urls.isEmpty else { return }
                model.upload(urls: urls)
            } label: {
                Label("Upload", systemImage: "square.and.arrow.up")
                    .font(.system(size: 12, weight: .medium))
            }
            .buttonStyle(.borderedProminent)
            .tint(theme.primary)
            .help("Upload files or folders")

            CapsuleSegments(
                options: [
                    (ViewMode.grid, "Grid", "square.grid.2x2"),
                    (ViewMode.list, "List", "list.bullet")
                ],
                selection: $model.viewMode,
                showLabels: false
            )

            sortMenu

            Button {
                Task { await model.refresh() }
            } label: {
                Image(systemName: "arrow.clockwise")
            }
            .help("Refresh")

            Button(allSelected ? "Unselect all" : "Select all") {
                if allSelected {
                    model.selection.removeAll()
                } else {
                    model.selection = Set(model.displayItems.map(\.id))
                }
            }
            .disabled(model.displayItems.isEmpty)

            Menu {
                Button("New Folder…") {
                    newFolderName = ""
                    showNewFolder = true
                }
            } label: {
                Image(systemName: "ellipsis.circle")
            }
            .menuStyle(.borderlessButton)
            .frame(width: 28)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 9)
    }

    private var allSelected: Bool {
        !model.displayItems.isEmpty && model.selection.count == model.displayItems.count
    }

    private var sortMenu: some View {
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
            Image(systemName: "arrow.up.arrow.down")
        }
        .menuStyle(.borderlessButton)
        .frame(width: 28)
        .help("Sort")
    }

    private var breadcrumb: some View {
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
                HStack(spacing: 3) {
                    ForEach(Array(model.breadcrumbs.enumerated()), id: \.offset) { index, crumb in
                        if index > 0 {
                            Image(systemName: "chevron.right")
                                .font(.system(size: 9))
                                .foregroundStyle(.tertiary)
                        }
                        Button {
                            model.navigate(toPrefix: crumb.prefix)
                        } label: {
                            Text(index == 0 ? "All Files" : crumb.name)
                                .font(.system(size: 13, weight: index == model.breadcrumbs.count - 1 ? .semibold : .regular))
                        }
                        .buttonStyle(.borderless)
                    }
                }
            }
            .frame(maxWidth: 320, alignment: .leading)
        }
    }

    // MARK: - Content

    @ViewBuilder
    private var content: some View {
        if model.isLoading && model.displayItems.isEmpty {
            VStack {
                Spacer()
                ProgressView("Loading…")
                Spacer()
            }
            .frame(maxWidth: .infinity)
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
        VStack {
            Spacer()
            VStack(spacing: 12) {
                Image(systemName: model.filterText.isEmpty ? "folder" : "magnifyingglass")
                    .font(.system(size: 38))
                    .foregroundStyle(theme.gradient)
                Text(model.filterText.isEmpty ? "This folder has no files yet" : "No matches")
                    .font(.title3.weight(.semibold))
                Text(
                    model.filterText.isEmpty
                        ? "Create a folder, drop files here, or use the button below\nto start building your object storage."
                        : "Nothing here matches “\(model.filterText)”."
                )
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                if model.filterText.isEmpty {
                    Button {
                        let urls = Panels.chooseFilesForUpload()
                        guard !urls.isEmpty else { return }
                        model.upload(urls: urls)
                    } label: {
                        Label("Upload Files", systemImage: "square.and.arrow.up")
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(theme.primary)
                    .controlSize(.large)
                    .padding(.top, 4)
                }
            }
            .frame(maxWidth: 380)
            .glassCard(cornerRadius: 16, padding: 34)
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }

    private var dropOverlay: some View {
        RoundedRectangle(cornerRadius: 12, style: .continuous)
            .strokeBorder(theme.primary, style: StrokeStyle(lineWidth: 3, dash: [8]))
            .background(
                theme.primary.opacity(0.07),
                in: RoundedRectangle(cornerRadius: 12, style: .continuous)
            )
            .overlay {
                Label(
                    "Drop to upload to \(model.bucket)/\(model.prefix)",
                    systemImage: "arrow.up.doc"
                )
                .font(.title3.weight(.medium))
                .padding(12)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10))
            }
            .padding(8)
            .allowsHitTesting(false)
    }

    // MARK: - Selection action bar

    private var selectionBar: some View {
        HStack(spacing: 4) {
            Text("\(model.selection.count) selected")
                .font(.callout.weight(.semibold))
                .padding(.trailing, 6)

            barButton("Download", symbol: "square.and.arrow.down") {
                handleAction(.download, model.selectedObjects)
            }
            barButton("Move…", symbol: "folder") {
                showMove = true
            }
            .disabled(model.selectedObjects.allSatisfy(\.isFolder))
            if model.selectedObjects.count == 1 {
                barButton("Rename…", symbol: "pencil") {
                    renameObject = model.selectedObjects.first
                }
                if let single = model.selectedObjects.first, !single.isFolder {
                    barButton("Copy Link", symbol: "link") {
                        model.copyPresignedLink(for: single)
                    }
                }
            } else {
                barButton("Batch Rename…", symbol: "pencil") {
                    showBatchRename = true
                }
                .disabled(model.selectedObjects.allSatisfy(\.isFolder))
            }
            barButton("Copy Keys", symbol: "doc.on.doc") {
                copyKeys(model.selectedObjects)
            }
            barButton("Delete", symbol: "trash", role: .destructive) {
                requestDelete(model.selectedObjects)
            }

            Divider().frame(height: 16).padding(.horizontal, 3)

            Button {
                model.selection.removeAll()
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .help("Clear selection")
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 9)
        .background(.regularMaterial, in: Capsule())
        .overlay(Capsule().strokeBorder(Color.primary.opacity(0.1), lineWidth: 1))
        .shadow(color: .black.opacity(0.14), radius: 12, y: 4)
    }

    private func barButton(
        _ title: String,
        symbol: String,
        role: ButtonRole? = nil,
        action: @escaping () -> Void
    ) -> some View {
        Button(role: role, action: action) {
            Label(title, systemImage: symbol)
                .font(.system(size: 12, weight: .medium))
                .padding(.horizontal, 8)
                .padding(.vertical, 5)
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .foregroundStyle(role == .destructive ? Color.red : Color.primary)
    }

    // MARK: - Status bar

    private var statusBar: some View {
        HStack(spacing: 12) {
            Text("\(model.folders.count) folders, \(model.files.count) files")
                .foregroundStyle(.secondary)
            if model.isTruncated {
                Button("Load more…") {
                    Task { await model.loadMore() }
                }
                .buttonStyle(.link)
            }
            if model.isBusy {
                ProgressView().controlSize(.mini)
            }
            Spacer()
            TransfersStatusLabel(transfers: model.transfers)
            if let synced = model.lastSynced {
                Text("Last sync \(synced.formatted(date: .omitted, time: .standard))")
                    .foregroundStyle(.tertiary)
            }
            Button {
                Task { await model.refresh() }
            } label: {
                Label("Sync", systemImage: "arrow.triangle.2.circlepath")
                    .font(.caption)
            }
            .buttonStyle(.borderless)
        }
        .font(.caption)
        .padding(.horizontal, 14)
        .padding(.vertical, 6)
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
            ToastCenter.shared.show(
                "Downloading \(objects.count) item\(objects.count == 1 ? "" : "s")",
                style: .info
            )
        case .rename:
            renameObject = objects.first
        case .delete:
            requestDelete(objects)
        case .copyKey:
            copyKeys(objects)
        case .copyLink:
            if let first = objects.first, !first.isFolder {
                model.copyPresignedLink(for: first)
            }
        case .move:
            model.selection = Set(objects.map(\.id))
            showMove = true
        case .metadata:
            if let first = objects.first, !first.isFolder {
                metadataObject = first
            }
        }
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
        ToastCenter.shared.show("Copied \(objects.count) key\(objects.count == 1 ? "" : "s")")
    }

    private var newFolderSheet: some View {
        VStack(spacing: 14) {
            Image(systemName: "folder.badge.plus")
                .font(.system(size: 28))
                .foregroundStyle(theme.gradient)
            Text("New Folder").font(.title3.weight(.semibold))
            TextField("Folder name", text: $newFolderName)
                .textFieldStyle(.roundedBorder)
                .frame(width: 260)
            HStack {
                Button("Cancel") { showNewFolder = false }
                    .keyboardShortcut(.cancelAction)
                Spacer()
                Button("Create") {
                    let name = newFolderName
                    showNewFolder = false
                    Task { await model.createFolder(named: name) }
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(newFolderName.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .frame(width: 260)
        }
        .padding(22)
    }
}

// MARK: - Shared bits

enum ObjectAction {
    case preview, download, rename, delete, copyKey, copyLink, move, metadata
}

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
