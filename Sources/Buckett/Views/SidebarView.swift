import SwiftUI

struct SidebarView: View {
    @EnvironmentObject private var appState: AppState
    @State private var showNewBucket = false
    @State private var newBucketName = ""
    @State private var newBucketError: String?

    var body: some View {
        VStack(spacing: 0) {
            accountPicker
                .padding(.horizontal, 12)
                .padding(.vertical, 8)

            List(selection: $appState.sidebarSelection) {
                Label("Dashboard", systemImage: "chart.bar.xaxis")
                    .tag(SidebarSelection.dashboard)

                Section("Buckets") {
                    if appState.bucketsLoading && appState.buckets.isEmpty {
                        HStack(spacing: 6) {
                            ProgressView().controlSize(.small)
                            Text("Loading…").foregroundStyle(.secondary)
                        }
                    }
                    ForEach(appState.buckets) { bucket in
                        Label(bucket.name, systemImage: "tray.full")
                            .tag(SidebarSelection.bucket(bucket.name))
                    }
                    if let error = appState.bucketsError {
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }
            }
            .listStyle(.sidebar)

            Divider()
            footer
        }
        .toolbar {
            ToolbarItem {
                Button {
                    Task { await appState.loadBuckets() }
                } label: {
                    Label("Refresh Buckets", systemImage: "arrow.clockwise")
                }
                .help("Refresh bucket list")
            }
            ToolbarItem {
                Button {
                    newBucketName = ""
                    newBucketError = nil
                    showNewBucket = true
                } label: {
                    Label("New Bucket", systemImage: "plus")
                }
                .help("Create a new bucket")
                .disabled(appState.currentClient == nil)
            }
        }
        .sheet(isPresented: $showNewBucket) {
            newBucketSheet
        }
    }

    private var accountPicker: some View {
        Group {
            if appState.accountStore.accounts.isEmpty {
                SettingsLink {
                    Label("Add Account…", systemImage: "person.crop.circle.badge.plus")
                        .frame(maxWidth: .infinity)
                }
            } else {
                Picker("Account", selection: Binding(
                    get: { appState.selectedAccountID },
                    set: { appState.selectAccount($0) }
                )) {
                    ForEach(appState.accountStore.accounts) { account in
                        Label(
                            account.name.isEmpty ? account.provider.displayName : account.name,
                            systemImage: account.provider.symbolName
                        )
                        .tag(Optional(account.id))
                    }
                }
                .labelsHidden()
            }
        }
    }

    private var footer: some View {
        HStack {
            if let account = appState.selectedAccount {
                Image(systemName: account.provider.symbolName)
                    .foregroundStyle(.secondary)
                Text(account.provider.displayName)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            SettingsLink {
                Image(systemName: "gearshape")
            }
            .buttonStyle(.borderless)
            .help("Settings")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var newBucketSheet: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("New Bucket").font(.headline)
            TextField("Bucket name", text: $newBucketName)
                .textFieldStyle(.roundedBorder)
                .frame(width: 280)
            if let error = newBucketError {
                Text(error).font(.caption).foregroundStyle(.red)
            }
            HStack {
                Spacer()
                Button("Cancel") { showNewBucket = false }
                    .keyboardShortcut(.cancelAction)
                Button("Create") {
                    let name = newBucketName.trimmingCharacters(in: .whitespaces)
                    guard !name.isEmpty else { return }
                    Task {
                        do {
                            try await appState.createBucket(named: name)
                            showNewBucket = false
                        } catch {
                            newBucketError = error.localizedDescription
                        }
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(newBucketName.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
    }
}
