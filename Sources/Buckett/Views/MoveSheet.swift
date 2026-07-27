import SwiftUI

/// "Move Files" dialog: pick a destination account and bucket (any account —
/// cross-account moves relay through this Mac), browse to a target folder or
/// type a path, choose a conflict strategy, then move.
struct MoveSheet: View {
    @ObservedObject var model: BrowserModel
    @EnvironmentObject private var appState: AppState
    @Environment(\.dismiss) private var dismiss
    @Environment(\.appTheme) private var theme

    @State private var destAccountID: UUID?
    @State private var destBucket = ""
    @State private var browsePrefix = ""
    @State private var foldersHere: [RemoteObject] = []
    @State private var loadingFolders = false
    @State private var customPath = ""
    @State private var strategy: BrowserModel.ConflictStrategy = .skip

    private var movingFiles: [RemoteObject] {
        model.selectedObjects.filter { !$0.isFolder }
    }

    private var destAccount: Account? {
        appState.accountStore.accounts.first { $0.id == destAccountID }
    }

    private var destClient: S3Client? {
        destAccount.flatMap { appState.client(for: $0) }
    }

    private var destBuckets: [Bucket] {
        destAccountID.map { appState.bucketList(for: $0) } ?? []
    }

    private var isCrossAccount: Bool {
        guard let destClient else { return false }
        return destClient !== model.client
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
                Text("Destination").font(.callout.weight(.medium))
                HStack(spacing: 8) {
                    Picker("Account", selection: $destAccountID) {
                        ForEach(appState.accountStore.accounts) { account in
                            Text(account.name.isEmpty ? account.provider.displayName : account.name)
                                .tag(Optional(account.id))
                        }
                    }
                    .labelsHidden()
                    Picker("Bucket", selection: $destBucket) {
                        ForEach(destBuckets) { bucket in
                            Text(displayName(for: bucket)).tag(bucket.name)
                        }
                    }
                    .labelsHidden()
                }
                if isCrossAccount {
                    Label(
                        "Different account — files transfer through this Mac (download, then upload).",
                        systemImage: "arrow.down.and.line.horizontal.and.arrow.up"
                    )
                    .font(.caption)
                    .foregroundStyle(.orange)
                }
            }
            .onChange(of: destAccountID) { _, _ in
                destBucket = destBuckets.first?.name ?? ""
                browsePrefix = ""
            }
            .onChange(of: destBucket) { _, _ in
                browsePrefix = ""
            }

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
                    startMove()
                } label: {
                    Label(
                        "Move \(movingFiles.count) object\(movingFiles.count == 1 ? "" : "s")",
                        systemImage: "folder.badge.plus"
                    )
                }
                .buttonStyle(.borderedProminent)
                .tint(theme.primary)
                .keyboardShortcut(.defaultAction)
                .disabled(movingFiles.isEmpty || destClient == nil || destBucket.isEmpty)
            }
        }
        .padding(22)
        .frame(width: 500)
        .onAppear {
            destAccountID = appState.selectedAccountID
            destBucket = model.bucket
        }
        .task(id: "\(destAccountID?.uuidString ?? "")|\(destBucket)|\(browsePrefix)") {
            await loadFolders()
        }
    }

    private func displayName(for bucket: Bucket) -> String {
        guard let id = destAccountID else { return bucket.name }
        return BucketAliases.shared.displayName(accountID: id, bucket: bucket.name)
    }

    private var targetDescription: String {
        let bucketLabel = destBucket.isEmpty ? "…" : destBucket
        return targetPrefix.isEmpty ? "root of \(bucketLabel)" : "\(bucketLabel)/\(targetPrefix)"
    }

    private func loadFolders() async {
        guard let destClient, !destBucket.isEmpty else {
            foldersHere = []
            return
        }
        loadingFolders = true
        foldersHere = (try? await destClient.listObjects(
            bucket: destBucket, prefix: browsePrefix
        ))?.folders ?? []
        loadingFolders = false
    }

    private func startMove() {
        guard let destClient, let destAccount, !destBucket.isEmpty else { return }
        let files = movingFiles
        let target = targetPrefix
        let chosen = strategy
        let bucketName = destBucket
        let display = "\(BucketAliases.shared.displayName(accountID: destAccount.id, bucket: bucketName))"
        dismiss()
        Task {
            await model.move(
                objects: files,
                destClient: destClient,
                destBucket: bucketName,
                toPrefix: target,
                strategy: chosen,
                destDisplayName: display
            )
        }
    }

    // MARK: - Folder browser

    private var folderBrowser: some View {
        VStack(spacing: 0) {
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
                                    .foregroundStyle(theme.secondary)
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
            .frame(height: 130)
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
