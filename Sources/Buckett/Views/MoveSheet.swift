import SwiftUI

/// "Move Files" dialog: pick a target folder (with drill-down browsing) or type a
/// path, choose a conflict strategy, then move via server-side copy + delete.
struct MoveSheet: View {
    @ObservedObject var model: BrowserModel
    @Environment(\.dismiss) private var dismiss

    @State private var browsePrefix = ""
    @State private var foldersHere: [RemoteObject] = []
    @State private var loadingFolders = false
    @State private var customPath = ""
    @State private var strategy: BrowserModel.ConflictStrategy = .skip

    private var movingFiles: [RemoteObject] {
        model.selectedObjects.filter { !$0.isFolder }
    }

    /// Effective destination prefix ("" = bucket root).
    private var targetPrefix: String {
        let typed = customPath.trimmingCharacters(in: .whitespacesAndNewlines)
        if !typed.isEmpty {
            var normalized = typed
            while normalized.hasPrefix("/") { normalized.removeFirst() }
            if !normalized.isEmpty && !normalized.hasSuffix("/") { normalized += "/" }
            return normalized
        }
        return browsePrefix
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("Move Files")
                    .font(.title2.weight(.semibold))
                Spacer()
                Button {
                    dismiss()
                } label: {
                    Image(systemName: "xmark")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction)
            }

            Label(
                "Moving \(movingFiles.count) file\(movingFiles.count == 1 ? "" : "s") to: \(targetDescription)",
                systemImage: "info.circle"
            )
            .font(.callout)
            .foregroundStyle(.secondary)

            VStack(alignment: .leading, spacing: 6) {
                Text("Select target folder").font(.callout.weight(.medium))
                folderBrowser
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("Or enter path").font(.callout.weight(.medium))
                TextField("e.g. images/2026/march/", text: $customPath)
                    .textFieldStyle(.roundedBorder)
                    .autocorrectionDisabled()
                Text("Leave empty to use the folder selected above.")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("Conflict handling").font(.callout.weight(.medium))
                Picker("", selection: $strategy) {
                    ForEach(BrowserModel.ConflictStrategy.allCases) { s in
                        Text("\(s.label) — \(s.detail)").tag(s)
                    }
                }
                .pickerStyle(.radioGroup)
                .labelsHidden()
            }

            HStack {
                Button("Cancel") { dismiss() }
                Spacer()
                Button {
                    let files = movingFiles
                    let target = targetPrefix
                    let chosen = strategy
                    dismiss()
                    Task { await model.move(objects: files, toPrefix: target, strategy: chosen) }
                } label: {
                    Label(
                        "Move \(movingFiles.count) object\(movingFiles.count == 1 ? "" : "s")",
                        systemImage: "folder.badge.plus"
                    )
                }
                .buttonStyle(.borderedProminent)
                .tint(Brand.indigo)
                .keyboardShortcut(.defaultAction)
                .disabled(movingFiles.isEmpty)
            }
        }
        .padding(22)
        .frame(width: 480)
        .task(id: browsePrefix) {
            loadingFolders = true
            foldersHere = await model.listFolders(at: browsePrefix)
            loadingFolders = false
        }
    }

    private var targetDescription: String {
        targetPrefix.isEmpty ? "root of \(model.bucket)" : targetPrefix
    }

    // MARK: - Folder browser

    private var folderBrowser: some View {
        VStack(spacing: 0) {
            // Breadcrumb inside the dialog
            HStack(spacing: 3) {
                Button {
                    browsePrefix = ""
                } label: {
                    Label("Root", systemImage: "tray.full")
                        .font(.caption.weight(browsePrefix.isEmpty ? .semibold : .regular))
                }
                .buttonStyle(.borderless)
                ForEach(Array(browseCrumbs.enumerated()), id: \.offset) { _, crumb in
                    Image(systemName: "chevron.right")
                        .font(.system(size: 8))
                        .foregroundStyle(.tertiary)
                    Button(crumb.name) {
                        browsePrefix = crumb.prefix
                    }
                    .buttonStyle(.borderless)
                    .font(.caption)
                }
                Spacer()
                if loadingFolders {
                    ProgressView().controlSize(.mini)
                }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            Divider().opacity(0.5)

            ScrollView {
                LazyVStack(spacing: 1) {
                    if foldersHere.isEmpty && !loadingFolders {
                        Text("No subfolders here — files will move into this folder.")
                            .font(.caption)
                            .foregroundStyle(.tertiary)
                            .padding(10)
                    }
                    ForEach(foldersHere) { folder in
                        Button {
                            browsePrefix = folder.key
                        } label: {
                            HStack(spacing: 7) {
                                Image(systemName: "folder")
                                    .foregroundStyle(Brand.teal)
                                Text(folder.name)
                                    .font(.callout)
                                Spacer()
                                Image(systemName: "chevron.right")
                                    .font(.system(size: 9))
                                    .foregroundStyle(.tertiary)
                            }
                            .padding(.horizontal, 10)
                            .padding(.vertical, 6)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .frame(height: 150)
        }
        .background(Color.primary.opacity(0.03))
        .clipShape(RoundedRectangle(cornerRadius: 9, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.09), lineWidth: 1)
        )
    }

    private var browseCrumbs: [(name: String, prefix: String)] {
        var crumbs: [(String, String)] = []
        var running = ""
        for component in browsePrefix.split(separator: "/") {
            running += component + "/"
            crumbs.append((String(component), running))
        }
        return crumbs
    }
}
