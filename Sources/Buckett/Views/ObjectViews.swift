import SwiftUI
import AppKit

// MARK: - Grid view

struct ObjectGridView: View {
    @ObservedObject var model: BrowserModel
    let onOpen: (RemoteObject) -> Void
    let onContextAction: (ObjectAction, [RemoteObject]) -> Void

    private let columns = [GridItem(.adaptive(minimum: 140, maximum: 200), spacing: 14)]

    var body: some View {
        ScrollView {
            LazyVGrid(columns: columns, spacing: 14) {
                ForEach(model.displayItems) { object in
                    ObjectGridItem(
                        object: object,
                        isSelected: model.selection.contains(object.id),
                        bucket: model.bucket,
                        client: model.client
                    )
                    .onTapGesture(count: 2) {
                        onOpen(object)
                    }
                    .onTapGesture {
                        select(object)
                    }
                    .contextMenu {
                        ObjectContextMenu(
                            objects: contextTargets(for: object),
                            onAction: onContextAction
                        )
                    }
                }
            }
            .padding(14)
        }
        .onTapGesture {
            model.selection.removeAll()
        }
    }

    private func select(_ object: RemoteObject) {
        let commandHeld = NSApp.currentEvent?.modifierFlags.contains(.command) ?? false
        if commandHeld {
            if model.selection.contains(object.id) {
                model.selection.remove(object.id)
            } else {
                model.selection.insert(object.id)
            }
        } else {
            model.selection = [object.id]
        }
    }

    /// Right-clicking a selected item acts on the whole selection; otherwise just that item.
    private func contextTargets(for object: RemoteObject) -> [RemoteObject] {
        if model.selection.contains(object.id) {
            return model.selectedObjects
        }
        return [object]
    }
}

struct ObjectGridItem: View {
    let object: RemoteObject
    let isSelected: Bool
    let bucket: String
    let client: S3Client

    @State private var thumbnail: NSImage?

    var body: some View {
        VStack(spacing: 6) {
            ZStack {
                RoundedRectangle(cornerRadius: 8)
                    .fill(Color.primary.opacity(0.045))
                if let thumbnail {
                    Image(nsImage: thumbnail)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                        .frame(width: 116, height: 84)
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                } else {
                    Image(systemName: object.symbolName)
                        .font(.system(size: 34))
                        .foregroundStyle(object.isFolder ? Color.accentColor : Color.secondary)
                }
            }
            .frame(height: 84)

            Text(object.name)
                .font(.callout)
                .lineLimit(2)
                .multilineTextAlignment(.center)
                .truncationMode(.middle)

            Text(object.isFolder ? "Folder" : object.formattedSize)
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .padding(8)
        .frame(maxWidth: .infinity)
        .background(
            RoundedRectangle(cornerRadius: 10)
                .fill(isSelected ? Color.accentColor.opacity(0.18) : Color.clear)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .strokeBorder(isSelected ? Color.accentColor : Color.clear, lineWidth: 1.5)
        )
        .contentShape(Rectangle())
        .task(id: object.id + (object.eTag ?? "")) {
            guard object.isImage else { return }
            thumbnail = await ThumbnailLoader.shared.thumbnail(
                for: object, bucket: bucket, client: client
            )
        }
    }
}

// MARK: - List view

struct ObjectListView: View {
    @ObservedObject var model: BrowserModel
    let onOpen: (RemoteObject) -> Void
    let onContextAction: (ObjectAction, [RemoteObject]) -> Void

    var body: some View {
        Table(model.displayItems, selection: $model.selection) {
            TableColumn("Name") { object in
                HStack(spacing: 7) {
                    Image(systemName: object.symbolName)
                        .foregroundStyle(object.isFolder ? Color.accentColor : Color.secondary)
                        .frame(width: 18)
                    Text(object.name)
                        .truncationMode(.middle)
                }
            }
            .width(min: 200, ideal: 340)

            TableColumn("Size") { object in
                Text(object.formattedSize)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
            }
            .width(min: 60, ideal: 90)

            TableColumn("Modified") { object in
                if let date = object.lastModified {
                    Text(date.formatted(date: .abbreviated, time: .shortened))
                        .foregroundStyle(.secondary)
                } else {
                    Text("—").foregroundStyle(.tertiary)
                }
            }
            .width(min: 120, ideal: 160)

            TableColumn("Kind") { object in
                Text(object.isFolder ? "Folder" : kindLabel(for: object))
                    .foregroundStyle(.secondary)
            }
            .width(min: 70, ideal: 100)
        }
        .contextMenu(forSelectionType: RemoteObject.ID.self) { ids in
            ObjectContextMenu(objects: objects(for: ids), onAction: onContextAction)
        } primaryAction: { ids in
            if let first = objects(for: ids).first {
                onOpen(first)
            }
        }
    }

    private func objects(for ids: Set<String>) -> [RemoteObject] {
        model.displayItems.filter { ids.contains($0.id) }
    }

    private func kindLabel(for object: RemoteObject) -> String {
        object.fileExtension.isEmpty ? "File" : object.fileExtension.uppercased()
    }
}

// MARK: - Shared context menu

struct ObjectContextMenu: View {
    let objects: [RemoteObject]
    let onAction: (ObjectAction, [RemoteObject]) -> Void

    private var singleFile: Bool {
        objects.count == 1 && !(objects.first?.isFolder ?? true)
    }

    var body: some View {
        if objects.isEmpty {
            EmptyView()
        } else {
            if singleFile {
                Button("Preview") { onAction(.preview, objects) }
                Button("Get Info") { onAction(.metadata, objects) }
                Divider()
            }
            Button("Download…") { onAction(.download, objects) }
            if objects.count == 1 {
                Button("Rename…") { onAction(.rename, objects) }
            }
            Button("Copy Key\(objects.count > 1 ? "s" : "")") { onAction(.copyKey, objects) }
            Divider()
            Button("Delete…", role: .destructive) { onAction(.delete, objects) }
        }
    }
}
