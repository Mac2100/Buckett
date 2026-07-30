import ServiceManagement
import SwiftUI

struct SettingsView: View {
    var body: some View {
        TabView {
            AccountsSettingsView()
                .tabItem { Label("Accounts", systemImage: "person.crop.circle") }
            AppearanceSettingsView()
                .tabItem { Label("Appearance", systemImage: "paintpalette") }
            GeneralSettingsView()
                .tabItem { Label("General", systemImage: "gearshape") }
            NotificationsSettingsView()
                .tabItem { Label("Notifications", systemImage: "bell.badge") }
            UpdatesSettingsView()
                .tabItem { Label("Updates", systemImage: "arrow.down.circle") }
            AboutSettingsView()
                .tabItem { Label("About", systemImage: "info.circle") }
        }
        .frame(width: 640, height: 460)
    }
}

struct AboutSettingsView: View {
    @Environment(\.appTheme) private var theme

    var body: some View {
        VStack(spacing: 12) {
            Spacer()
            theme.glyph(size: 64)
            Text("Buckett")
                .font(.title.weight(.bold))
            Text("Version \(AppVersion.current)")
                .foregroundStyle(.secondary)
            Text("An open-source visual bucket explorer for\nCloudflare R2 and Backblaze B2.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            HStack(spacing: 16) {
                Link("GitHub", destination: URL(string: "https://github.com/\(UpdateChecker.repo)")!)
                Link("Releases", destination: UpdateChecker.releasesPage)
                Link(
                    "MIT License",
                    destination: URL(string: "https://github.com/\(UpdateChecker.repo)/blob/main/LICENSE")!
                )
            }
            .padding(.top, 6)
            BuyMeACoffeeButton()
                .padding(.top, 10)
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }
}

// MARK: - Accounts

struct AccountsSettingsView: View {
    @EnvironmentObject private var appState: AppState
    @State private var selectedID: UUID?
    @State private var accountPendingDeletion: Account?

    var body: some View {
        HStack(spacing: 0) {
            VStack(spacing: 0) {
                List(appState.accountStore.accounts, selection: $selectedID) { account in
                    Label(
                        account.name.isEmpty ? "Untitled" : account.name,
                        systemImage: account.provider.symbolName
                    )
                    .tag(account.id)
                }
                .listStyle(.plain)

                Divider()
                HStack(spacing: 2) {
                    Button {
                        var account = Account()
                        account.name = "New Account"
                        appState.saveAccount(account, secretKey: nil)
                        selectedID = account.id
                    } label: {
                        Image(systemName: "plus")
                    }
                    Button {
                        if let account = selectedAccount {
                            accountPendingDeletion = account
                        }
                    } label: {
                        Image(systemName: "minus")
                    }
                    .disabled(selectedAccount == nil)
                    Spacer()
                }
                .buttonStyle(.borderless)
                .padding(6)
            }
            .frame(width: 190)

            Divider()

            if let account = selectedAccount {
                AccountEditorView(account: account) { updated, secret in
                    appState.saveAccount(updated, secretKey: secret)
                }
                .id(account.id)
            } else {
                VStack(spacing: 8) {
                    Image(systemName: "person.crop.circle.badge.questionmark")
                        .font(.system(size: 36))
                        .foregroundStyle(.tertiary)
                    Text("Select an account, or add one with +")
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .onAppear {
            if selectedID == nil {
                selectedID = appState.accountStore.accounts.first?.id
            }
        }
        .confirmationDialog(
            "Remove “\(accountPendingDeletion?.name ?? "")”? Its credentials will be deleted from the Keychain.",
            isPresented: Binding(
                get: { accountPendingDeletion != nil },
                set: { if !$0 { accountPendingDeletion = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button("Remove Account", role: .destructive) {
                if let account = accountPendingDeletion {
                    appState.deleteAccount(account)
                    selectedID = appState.accountStore.accounts.first?.id
                }
                accountPendingDeletion = nil
            }
            Button("Cancel", role: .cancel) { accountPendingDeletion = nil }
        }
    }

    private var selectedAccount: Account? {
        appState.accountStore.accounts.first { $0.id == selectedID }
    }
}

struct AccountEditorView: View {
    let account: Account
    let onSave: (Account, String?) -> Void

    @State private var name: String
    @State private var provider: Provider
    @State private var cloudflareAccountID: String
    @State private var b2Region: String
    @State private var customEndpoint: String
    @State private var accessKeyID: String
    @State private var secretKey: String
    @State private var secretChanged = false
    @State private var testResult: TestResult?
    @State private var testing = false

    enum TestResult {
        case success(Int)
        case failure(String)
    }

    init(account: Account, onSave: @escaping (Account, String?) -> Void) {
        self.account = account
        self.onSave = onSave
        _name = State(initialValue: account.name)
        _provider = State(initialValue: account.provider)
        _cloudflareAccountID = State(initialValue: account.cloudflareAccountID)
        _b2Region = State(initialValue: account.b2Region)
        _customEndpoint = State(initialValue: account.customEndpoint)
        _accessKeyID = State(initialValue: account.accessKeyID)
        _secretKey = State(initialValue: Keychain.secret(for: account.id) ?? "")
    }

    var body: some View {
        Form {
            Section {
                TextField("Display Name", text: $name)
                Picker("Provider", selection: $provider) {
                    ForEach(Provider.allCases) { p in
                        Text(p.displayName).tag(p)
                    }
                }
                .pickerStyle(.segmented)
            }

            Section {
                switch provider {
                case .cloudflareR2:
                    TextField("Cloudflare Account ID", text: $cloudflareAccountID)
                        .help("Found in the Cloudflare dashboard URL or the R2 overview page")
                case .backblazeB2:
                    TextField("Region (e.g. us-west-004)", text: $b2Region)
                        .help("The region from your bucket's S3 endpoint: s3.<region>.backblazeb2.com")
                case .amazonS3:
                    TextField("Region (e.g. us-east-1)", text: $b2Region)
                        .help("The AWS region your buckets live in")
                }
                TextField("Custom Endpoint (optional)", text: $customEndpoint)
                    .help("Overrides the derived endpoint. Any S3-compatible endpoint works.")
                if let endpoint = draft.endpointURL {
                    LabeledContent("Endpoint") {
                        Text(endpoint.absoluteString)
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                    }
                }
            } header: {
                Text("Connection")
            }

            Section {
                TextField("Access Key ID", text: $accessKeyID)
                    .autocorrectionDisabled()
                SecureField("Secret Access Key", text: $secretKey)
                    .onChange(of: secretKey) { _, _ in secretChanged = true }
            } header: {
                Text("Credentials")
            } footer: {
                Text("Credentials are stored only in your macOS Keychain and are used solely to sign API requests sent directly to the provider.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Section {
                HStack {
                    Button {
                        testConnection()
                    } label: {
                        if testing {
                            ProgressView().controlSize(.small)
                        } else {
                            Text("Test Connection")
                        }
                    }
                    .disabled(testing || !canTest)

                    switch testResult {
                    case .success(let count):
                        Label("Connected — \(count) bucket\(count == 1 ? "" : "s") visible", systemImage: "checkmark.circle.fill")
                            .foregroundStyle(.green)
                    case .failure(let message):
                        Label(message, systemImage: "xmark.circle.fill")
                            .foregroundStyle(.red)
                            .lineLimit(2)
                    case nil:
                        EmptyView()
                    }
                    Spacer()
                    Button("Save") {
                        onSave(draft, secretChanged ? secretKey : nil)
                    }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
                }
            }
        }
        .formStyle(.grouped)
    }

    private var draft: Account {
        var updated = account
        updated.name = name
        updated.provider = provider
        updated.cloudflareAccountID = cloudflareAccountID
        updated.b2Region = b2Region
        updated.customEndpoint = customEndpoint
        updated.accessKeyID = accessKeyID.trimmingCharacters(in: .whitespaces)
        return updated
    }

    private var canTest: Bool {
        draft.endpointURL != nil && !draft.accessKeyID.isEmpty && !secretKey.isEmpty
    }

    private func testConnection() {
        guard let client = S3Client(account: draft, secretKey: secretKey) else {
            testResult = .failure("Endpoint is not configured")
            return
        }
        testing = true
        testResult = nil
        Task {
            do {
                let buckets = try await client.listBuckets()
                testResult = .success(buckets.count)
            } catch {
                testResult = .failure(error.localizedDescription)
            }
            testing = false
        }
    }
}

// MARK: - Appearance

struct AppearanceSettingsView: View {
    @EnvironmentObject private var themeStore: ThemeStore
    @AppStorage("menuBarSymbol") private var menuBarSymbol = MenuBarIconStyle.archive.rawValue

    private let swatchColumns = [GridItem(.adaptive(minimum: 108, maximum: 150), spacing: 12)]

    var body: some View {
        Form {
            Picker("Appearance", selection: $themeStore.appearance) {
                ForEach(AppearanceMode.allCases) { mode in
                    Text(mode.label).tag(mode)
                }
            }
            .pickerStyle(.segmented)

            Section("Theme") {
                LazyVGrid(columns: swatchColumns, spacing: 12) {
                    ForEach(Themes.all) { theme in
                        themeSwatch(theme)
                    }
                }
                .padding(.vertical, 4)
            }

            Section("Menu Bar Icon") {
                HStack(spacing: 10) {
                    ForEach(MenuBarIconStyle.allCases) { style in
                        iconSwatch(style)
                    }
                }
                .padding(.vertical, 4)
                Text("Used for the menu bar drop target and its animations.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onChange(of: menuBarSymbol) { _, _ in
            MenuBarController.shared.refreshIcon()
        }
    }

    private func iconSwatch(_ style: MenuBarIconStyle) -> some View {
        let isSelected = menuBarSymbol == style.rawValue
        return Button {
            menuBarSymbol = style.rawValue
        } label: {
            VStack(spacing: 6) {
                Image(systemName: style.rawValue)
                    .font(.system(size: 19))
                    .frame(height: 24)
                Text(style.label)
                    .font(.caption2)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 9)
            .background(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(
                        isSelected
                            ? themeStore.theme.primary.opacity(0.12)
                            : Color.primary.opacity(0.03)
                    )
            )
            .overlay(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .strokeBorder(
                        isSelected
                            ? themeStore.theme.primary.opacity(0.7)
                            : Color.primary.opacity(0.07),
                        lineWidth: isSelected ? 1.5 : 1
                    )
            )
            .contentShape(RoundedRectangle(cornerRadius: 9))
        }
        .buttonStyle(.plain)
    }

    private func themeSwatch(_ theme: AppTheme) -> some View {
        let isSelected = themeStore.themeID == theme.id
        return Button {
            withAnimation(.snappy(duration: 0.15)) {
                themeStore.themeID = theme.id
            }
        } label: {
            VStack(spacing: 8) {
                Circle()
                    .fill(theme.gradient)
                    .frame(width: 38, height: 38)
                    .overlay {
                        if isSelected {
                            Image(systemName: "checkmark")
                                .font(.system(size: 14, weight: .bold))
                                .foregroundStyle(.white)
                        }
                    }
                Text(theme.name)
                    .font(.callout)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 10)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(isSelected ? theme.primary.opacity(0.1) : Color.primary.opacity(0.03))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(
                        isSelected ? theme.primary.opacity(0.7) : Color.primary.opacity(0.07),
                        lineWidth: isSelected ? 1.5 : 1
                    )
            )
            .contentShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
    }
}

// MARK: - General

struct GeneralSettingsView: View {
    @AppStorage("defaultViewMode") private var defaultViewMode = ViewMode.grid.rawValue
    @AppStorage("maxConcurrentTransfers") private var maxConcurrentTransfers = 3
    @AppStorage("showMenuBarIcon") private var showMenuBarIcon = true
    @AppStorage("openAtLogin") private var openAtLogin = false

    var body: some View {
        Form {
            Picker("Default view", selection: $defaultViewMode) {
                ForEach(ViewMode.allCases) { mode in
                    Text(mode.label).tag(mode.rawValue)
                }
            }
            Stepper(value: $maxConcurrentTransfers, in: 1...8) {
                LabeledContent("Concurrent transfers", value: "\(maxConcurrentTransfers)")
            }
            Section {
                Toggle("Show menu bar drop target", isOn: $showMenuBarIcon)
                    .onChange(of: showMenuBarIcon) { _, visible in
                        MenuBarController.shared.setVisible(visible)
                    }
                Text("Drag files onto the bucket icon in the menu bar to upload them instantly. Right there in the icon's menu you can pick which bucket drops go to.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Section {
                Toggle("Open at login", isOn: $openAtLogin)
                    .onChange(of: openAtLogin) { _, enabled in
                        do {
                            if enabled {
                                try SMAppService.mainApp.register()
                            } else {
                                try SMAppService.mainApp.unregister()
                            }
                        } catch {
                            ToastCenter.shared.show(
                                "Couldn't update login item",
                                detail: error.localizedDescription,
                                style: .error
                            )
                        }
                    }
            }
            Text("Uploads of 16 MB or more automatically use resumable multipart uploads. Interrupted uploads resume from the last completed part when retried.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .formStyle(.grouped)
    }
}

// MARK: - Notifications

struct NotificationsSettingsView: View {
    @AppStorage("notifyTransfersComplete") private var transfersComplete = true
    @AppStorage("notifyTransferFailed") private var transferFailed = true
    @AppStorage("notifyDropStarted") private var dropStarted = false
    @AppStorage("showToasts") private var showToasts = true

    var body: some View {
        Form {
            Section {
                Toggle("All transfers finished", isOn: $transfersComplete)
                Toggle("A transfer fails", isOn: $transferFailed)
                Toggle("Menu bar drop starts uploading", isOn: $dropStarted)
            } header: {
                Text("System Notifications")
            } footer: {
                Text("Delivered through macOS Notification Center — allow notifications for Buckett when macOS asks the first time.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Section("In-App") {
                Toggle("Show toast banners", isOn: $showToasts)
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Updates

struct UpdatesSettingsView: View {
    @EnvironmentObject private var appState: AppState
    @AppStorage("autoCheckUpdates") private var autoCheckUpdates = true

    var body: some View {
        Form {
            LabeledContent("Current version", value: AppVersion.current)
            Toggle("Check for updates at launch", isOn: $autoCheckUpdates)

            UpdateStatusView(updates: appState.updates)

            Link("View releases on GitHub", destination: UpdateChecker.releasesPage)
                .font(.callout)
        }
        .formStyle(.grouped)
    }
}

struct UpdateStatusView: View {
    @ObservedObject var updates: UpdateChecker
    @ObservedObject private var updater = SelfUpdater.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                Button {
                    Task { await updates.check(userInitiated: true) }
                } label: {
                    if updates.status == .checking {
                        ProgressView().controlSize(.small)
                    } else {
                        Text("Check Now")
                    }
                }
                .disabled(updates.status == .checking || updater.isBusy)

                switch updates.status {
                case .idle, .checking:
                    EmptyView()
                case .upToDate:
                    Label("You're up to date", systemImage: "checkmark.circle.fill")
                        .foregroundStyle(.green)
                case .noReleasesVisible:
                    Label(
                        "No releases visible — private repositories can't be checked anonymously",
                        systemImage: "eye.slash"
                    )
                    .foregroundStyle(.orange)
                case .updateAvailable(let version, let url):
                    Label("Version \(version) is available", systemImage: "arrow.down.circle.fill")
                        .foregroundStyle(.blue)
                    Button("Install & Relaunch") {
                        SelfUpdater.shared.install(from: url)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(updater.isBusy)
                case .failed(let message):
                    Label(message, systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(.orange)
                        .lineLimit(2)
                }
                Spacer()
                if let lastChecked = updates.lastChecked {
                    Text("Last checked \(lastChecked.formatted(date: .omitted, time: .shortened))")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }

            switch updater.phase {
            case .idle:
                EmptyView()
            case .downloading:
                Label {
                    Text("Downloading update…")
                } icon: {
                    ProgressView().controlSize(.small)
                }
                .foregroundStyle(.secondary)
            case .installing:
                Label {
                    Text("Installing…")
                } icon: {
                    ProgressView().controlSize(.small)
                }
                .foregroundStyle(.secondary)
            case .relaunching:
                Label("Relaunching…", systemImage: "arrow.triangle.2.circlepath")
                    .foregroundStyle(.secondary)
            case .failed(let message):
                Label(message, systemImage: "xmark.circle.fill")
                    .foregroundStyle(.red)
                    .lineLimit(2)
            }
        }
    }
}
