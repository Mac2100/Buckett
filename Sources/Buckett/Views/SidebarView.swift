import SwiftUI

struct SidebarView: View {
    @EnvironmentObject private var appState: AppState
    @State private var showNewBucket = false

    var body: some View {
        VStack(spacing: 0) {
            header
            accountSwitcher
                .padding(.horizontal, 12)
                .padding(.top, 4)

            ScrollView {
                LazyVStack(alignment: .leading, spacing: 6) {
                    overviewRow
                        .padding(.top, 12)

                    HStack {
                        Text("BUCKETS")
                            .font(.caption2.weight(.semibold))
                            .foregroundStyle(.secondary)
                        Spacer()
                        if appState.bucketsLoading {
                            ProgressView().controlSize(.mini)
                        }
                        Button {
                            Task { await appState.loadBuckets() }
                        } label: {
                            Image(systemName: "arrow.clockwise")
                                .font(.system(size: 10, weight: .semibold))
                        }
                        .buttonStyle(.borderless)
                        .help("Refresh buckets")
                        Button {
                            showNewBucket = true
                        } label: {
                            Image(systemName: "plus")
                                .font(.system(size: 11, weight: .semibold))
                        }
                        .buttonStyle(.borderless)
                        .help("New bucket")
                        .disabled(appState.currentClient == nil)
                    }
                    .padding(.top, 12)
                    .padding(.horizontal, 4)

                    ForEach(appState.buckets) { bucket in
                        BucketRowCard(
                            bucket: bucket,
                            stats: appState.stats[bucket.name],
                            isSelected: appState.sidebarSelection == .bucket(bucket.name)
                        ) {
                            appState.sidebarSelection = .bucket(bucket.name)
                        }
                    }

                    if let error = appState.bucketsError {
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                            .padding(6)
                    }

                    if appState.buckets.isEmpty && !appState.bucketsLoading
                        && appState.bucketsError == nil && appState.selectedAccount != nil {
                        Text("No buckets yet. Create one with +")
                            .font(.caption)
                            .foregroundStyle(.tertiary)
                            .padding(6)
                    }
                }
                .padding(.horizontal, 10)
            }

            Divider().opacity(0.4)
            footer
        }
        .sheet(isPresented: $showNewBucket) {
            NewBucketSheet()
        }
    }

    private var header: some View {
        HStack(spacing: 9) {
            Brand.glyph(size: 28)
            Text("Buckett")
                .font(.title3.weight(.bold))
            Spacer()
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
    }

    @ViewBuilder
    private var accountSwitcher: some View {
        if appState.accountStore.accounts.isEmpty {
            Button {
                appState.showOnboarding = true
            } label: {
                Label("Add Account…", systemImage: "person.crop.circle.badge.plus")
                    .frame(maxWidth: .infinity)
            }
            .controlSize(.large)
        } else {
            Menu {
                ForEach(appState.accountStore.accounts) { account in
                    Button {
                        appState.selectAccount(account.id)
                    } label: {
                        HStack {
                            Image(systemName: account.provider.symbolName)
                            Text(account.name.isEmpty ? account.provider.displayName : account.name)
                            if account.id == appState.selectedAccountID {
                                Image(systemName: "checkmark")
                            }
                        }
                    }
                }
                Divider()
                Button("Add Account…") {
                    appState.showOnboarding = true
                }
            } label: {
                HStack(spacing: 8) {
                    Image(systemName: appState.selectedAccount?.provider.symbolName ?? "cloud")
                        .foregroundStyle(Brand.gradient)
                    VStack(alignment: .leading, spacing: 0) {
                        Text(currentAccountName)
                            .font(.callout.weight(.medium))
                            .lineLimit(1)
                        if let account = appState.selectedAccount {
                            Text(account.provider.displayName)
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                        }
                    }
                    Spacer()
                    Image(systemName: "chevron.up.chevron.down")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                .contentShape(RoundedRectangle(cornerRadius: 9))
            }
            .menuStyle(.borderlessButton)
        }
    }

    private var currentAccountName: String {
        guard let account = appState.selectedAccount else { return "Select account" }
        return account.name.isEmpty ? account.provider.displayName : account.name
    }

    private var overviewRow: some View {
        Button {
            appState.sidebarSelection = .dashboard
        } label: {
            HStack(spacing: 8) {
                Image(systemName: "chart.bar.xaxis")
                    .frame(width: 18)
                Text("Overview")
                    .font(.callout.weight(.medium))
                Spacer()
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .background(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(
                        appState.sidebarSelection == .dashboard
                            ? Color.accentColor.opacity(0.16)
                            : Color.clear
                    )
            )
            .contentShape(RoundedRectangle(cornerRadius: 9))
        }
        .buttonStyle(.plain)
    }

    private var footer: some View {
        HStack {
            SettingsLink {
                Label("Settings", systemImage: "gearshape")
                    .font(.callout)
            }
            .buttonStyle(.borderless)
            Spacer()
            Text("v\(AppVersion.current)")
                .font(.caption2)
                .foregroundStyle(.tertiary)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}

// MARK: - Bucket card

struct BucketRowCard: View {
    let bucket: Bucket
    let stats: BucketStats?
    let isSelected: Bool
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 7) {
                HStack(spacing: 8) {
                    Image(systemName: "tray.full.fill")
                        .foregroundStyle(isSelected ? AnyShapeStyle(Brand.gradient) : AnyShapeStyle(.secondary))
                    Text(bucket.name)
                        .font(.callout.weight(.semibold))
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Spacer()
                }
                if let stats {
                    HStack(spacing: 4) {
                        Image(systemName: "internaldrive")
                            .font(.system(size: 9))
                        Text("\(stats.formattedSize) · \(stats.objectCount) objects")
                    }
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                } else if let created = bucket.creationDate {
                    Text("Created \(created.formatted(date: .abbreviated, time: .omitted))")
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(
                        isSelected
                            ? Color.accentColor.opacity(0.15)
                            : hovering ? Color.primary.opacity(0.06) : Color.primary.opacity(0.035)
                    )
            )
            .overlay(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(
                        isSelected ? Color.accentColor.opacity(0.55) : Color.primary.opacity(0.06),
                        lineWidth: 1
                    )
            )
            .contentShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.12), value: hovering)
    }
}

// MARK: - New bucket sheet

struct NewBucketSheet: View {
    @EnvironmentObject private var appState: AppState
    @Environment(\.dismiss) private var dismiss

    @State private var name = ""
    @State private var creating = false
    @State private var errorMessage: String?

    private var trimmed: String {
        name.trimmingCharacters(in: .whitespaces)
    }

    private var isValidName: Bool {
        let pattern = "^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$"
        return trimmed.range(of: pattern, options: .regularExpression) != nil
    }

    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: "tray.full.fill")
                .font(.system(size: 30))
                .foregroundStyle(Brand.gradient)
            Text("New Bucket")
                .font(.title3.weight(.semibold))

            VStack(alignment: .leading, spacing: 6) {
                TextField("bucket-name", text: $name)
                    .textFieldStyle(.roundedBorder)
                    .autocorrectionDisabled()
                Text("3–63 characters; lowercase letters, numbers, and hyphens; must start and end with a letter or number.")
                    .font(.caption)
                    .foregroundStyle(
                        trimmed.isEmpty || isValidName ? Color.secondary : Color.orange
                    )
                if let account = appState.selectedAccount {
                    Text(
                        account.provider == .cloudflareR2
                            ? "Region: automatic — R2 places the bucket close to where you create it."
                            : "Created in your account region (\(account.signingRegion))."
                    )
                    .font(.caption)
                    .foregroundStyle(.tertiary)
                }
            }
            .frame(width: 320)

            if let errorMessage {
                Text(errorMessage)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .frame(width: 320)
            }

            HStack {
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Spacer()
                Button {
                    create()
                } label: {
                    if creating {
                        ProgressView().controlSize(.small).frame(minWidth: 60)
                    } else {
                        Text("Create").frame(minWidth: 60)
                    }
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(!isValidName || creating)
            }
            .frame(width: 320)
        }
        .padding(24)
    }

    private func create() {
        creating = true
        errorMessage = nil
        let bucketName = trimmed
        Task {
            do {
                try await appState.createBucket(named: bucketName)
                dismiss()
                ToastCenter.shared.show("Bucket created", detail: bucketName)
                appState.sidebarSelection = .bucket(bucketName)
            } catch {
                errorMessage = error.localizedDescription
            }
            creating = false
        }
    }
}
