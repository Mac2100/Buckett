import SwiftUI
import AppKit

// MARK: - Grid view

struct ObjectGridView: View {
    @ObservedObject var model: BrowserModel
    let onOpen: (RemoteObject) -> Void
    let onContextAction: (ObjectAction, [RemoteObject]) -> Void

    private let columns = [GridItem(.adaptive(minimum: 168, maximum: 230), spacing: 14)]

    var body: some View {
        ScrollView {
            LazyVGrid(columns: columns, spacing: 14) {
                ForEach(model.displayItems) { object in
                    ObjectGridItem(
                        object: object,
                        isSelected: model.selection.contains(object.id),
                        bucket: model.bucket,
                        client: model.client,
                        onOpen: { onOpen(object) },
                        onSelect: { select(object) }
                    )
                    .contextMenu {
                        ObjectContextMenu(
                            objects: contextTargets(for: object),
                            onAction: onContextAction
                        )
                    }
                }
            }
            .padding(16)
            .padding(.bottom, 56) // keep the selection bar clear of the last row
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

    private func contextTargets(for object: RemoteObject) -> [RemoteObject] {
        if model.selection.contains(object.id) {
            return model.selectedObjects
        }
        return [object]
    }
}

struct ObjectGridItem: View {
    @Environment(\.appTheme) private var theme
    let object: RemoteObject
    let isSelected: Bool
    let bucket: String
    let client: S3Client
    let onOpen: () -> Void
    let onSelect: () -> Void

    @State private var thumbnail: NSImage?
    @State private var hovering = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            thumbnailArea
            VStack(alignment: .leading, spacing: 2) {
                Text(object.name)
                    .font(.callout.weight(.medium))
                    .lineLimit(1)
                    .truncationMode(.middle)
                Text(subtitle)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 11)
            .padding(.vertical, 9)
        }
        .background(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(Color(nsColor: .controlBackgroundColor))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(
                    isSelected ? Color.accentColor : Color.primary.opacity(0.08),
                    lineWidth: isSelected ? 2 : 1
                )
        )
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        .shadow(color: .black.opacity(hovering ? 0.10 : 0.05), radius: hovering ? 8 : 4, y: 2)
        .scaleEffect(hovering ? 1.012 : 1)
        .animation(.easeOut(duration: 0.12), value: hovering)
        .contentShape(RoundedRectangle(cornerRadius: 12))
        .onHover { hovering = $0 }
        .onTapGesture(count: 2) { onOpen() }
        .onTapGesture { onSelect() }
        .task(id: object.id + (object.eTag ?? "")) {
            guard object.isImage else { return }
            thumbnail = await ThumbnailLoader.shared.thumbnail(
                for: object, bucket: bucket, client: client
            )
        }
    }

    private var subtitle: String {
        if object.isFolder { return "Folder" }
        if let date = object.lastModified {
            return "\(object.formattedSize) · \(date.formatted(date: .numeric, time: .shortened))"
        }
        return object.formattedSize
    }

    private var thumbnailArea: some View {
        ZStack {
            Rectangle()
                .fill(Color.primary.opacity(0.045))
            if let thumbnail {
                Image(nsImage: thumbnail)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
            } else {
                Image(systemName: object.symbolName)
                    .font(.system(size: 36))
                    .foregroundStyle(
                        object.isFolder ? AnyShapeStyle(theme.gradient) : AnyShapeStyle(.secondary)
                    )
            }
        }
        .frame(height: 106)
        .clipped()
        .overlay(alignment: .topLeading) {
            if isSelected {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 17))
                    .foregroundStyle(.white, Color.accentColor)
                    .padding(6)
            }
        }
        .overlay(alignment: .bottomLeading) {
            if !object.isFolder && object.size > 0 {
                Text(object.formattedSize)
                    .font(.system(size: 9, weight: .semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 5)
                    .padding(.vertical, 2)
                    .background(.black.opacity(0.55), in: Capsule())
                    .padding(5)
            }
        }
        .overlay {
            if hovering {
                Text(object.isFolder ? "Open folder" : "Preview")
                    .font(.caption.weight(.medium))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(.regularMaterial, in: Capsule())
                    .allowsHitTesting(false)
            }
        }
    }
}

// MARK: - List view

struct ObjectListView: View {
    @ObservedObject var model: BrowserModel
    let onOpen: (RemoteObject) -> Void
    let onContextAction: (ObjectAction, [RemoteObject]) -> Void

    /// Bridges the table's clickable-header sort state to the model's
    /// sortField/sortAscending (the single source also used by the grid
    /// and the sort menu), so all three stay in sync.
    private var tableSort: Binding<[KeyPathComparator<RemoteObject>]> {
        Binding(
            get: {
                let order: SortOrder = model.sortAscending ? .forward : .reverse
                switch model.sortField {
                case .name: return [KeyPathComparator(\RemoteObject.name, order: order)]
                case .size: return [KeyPathComparator(\RemoteObject.size, order: order)]
                case .date: return [KeyPathComparator(\RemoteObject.sortDate, order: order)]
                case .kind: return [KeyPathComparator(\RemoteObject.fileExtension, order: order)]
                }
            },
            set: { comparators in
                guard let first = comparators.first else { return }
                model.sortAscending = first.order == .forward
                let keyPath = first.keyPath
                if keyPath == \RemoteObject.name {
                    model.sortField = .name
                } else if keyPath == \RemoteObject.size {
                    model.sortField = .size
                } else if keyPath == \RemoteObject.sortDate {
                    model.sortField = .date
                } else if keyPath == \RemoteObject.fileExtension {
                    model.sortField = .kind
                }
            }
        )
    }

    var body: some View {
        Table(model.displayItems, selection: $model.selection, sortOrder: tableSort) {
            TableColumn("Name", value: \.name) { object in
                HStack(spacing: 7) {
                    Image(systemName: object.symbolName)
                        .foregroundStyle(object.isFolder ? Color.accentColor : Color.secondary)
                        .frame(width: 18)
                    Text(object.name)
                        .truncationMode(.middle)
                }
            }
            .width(min: 200, ideal: 340)

            TableColumn("Size", value: \.size) { object in
                Text(object.formattedSize)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
            }
            .width(min: 60, ideal: 90)

            TableColumn("Modified", value: \.sortDate) { object in
                if let date = object.lastModified {
                    Text(date.formatted(date: .abbreviated, time: .shortened))
                        .foregroundStyle(.secondary)
                } else {
                    Text("—").foregroundStyle(.tertiary)
                }
            }
            .width(min: 120, ideal: 160)

            TableColumn("Kind", value: \.fileExtension) { object in
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
                Button("Copy Share Link (7 days)") { onAction(.copyLink, objects) }
                Divider()
            }
            Button("Download…") { onAction(.download, objects) }
            if objects.contains(where: { !$0.isFolder }) {
                Button("Move To…") { onAction(.move, objects) }
            }
            if objects.count == 1 {
                Button("Rename…") { onAction(.rename, objects) }
            }
            Button("Copy Key\(objects.count > 1 ? "s" : "")") { onAction(.copyKey, objects) }
            Divider()
            Button("Delete…", role: .destructive) { onAction(.delete, objects) }
        }
    }
}
