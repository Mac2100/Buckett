import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var appState: AppState

    var body: some View {
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 220, ideal: 250, max: 320)
        } detail: {
            detail
        }
        .overlay(ToastHostView())
        .background(UpdateAlertHost(updates: appState.updates))
        .sheet(isPresented: $appState.showOnboarding) {
            OnboardingView()
        }
        .navigationTitle("Buckett")
    }

    @ViewBuilder
    private var detail: some View {
        if appState.accountStore.accounts.isEmpty {
            WelcomeView()
        } else {
            switch appState.sidebarSelection {
            case .bucket(let name):
                if let client = appState.currentClient {
                    BucketDetailView(bucket: name, client: client)
                        .id("\(appState.selectedAccountID?.uuidString ?? "none")/\(name)")
                } else {
                    MissingCredentialsView()
                }
            case .dashboard, .none:
                DashboardView()
            }
        }
    }
}

// MARK: - Bucket detail with top tabs (Files / Transfers / Statistics)

enum BucketTab: String, CaseIterable {
    case files, transfers, statistics
}

struct BucketDetailView: View {
    @EnvironmentObject private var appState: AppState
    let bucket: String
    let client: S3Client

    @State private var tab: BucketTab = .files

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Spacer()
                CapsuleSegments(
                    options: [
                        (BucketTab.files, "Files", "folder"),
                        (BucketTab.transfers, "Transfers", "arrow.up.arrow.down"),
                        (BucketTab.statistics, "Statistics", "chart.bar")
                    ],
                    selection: $tab
                )
                Spacer()
            }
            .padding(.vertical, 10)

            Divider().opacity(0.4)

            switch tab {
            case .files:
                BrowserView(bucket: bucket, client: client, transfers: appState.transfers)
            case .transfers:
                TransfersView(transfers: appState.transfers)
            case .statistics:
                StatisticsView(bucket: bucket)
            }
        }
        .navigationTitle(bucket)
    }
}

// MARK: - Update alert

struct UpdateAlertHost: View {
    @ObservedObject var updates: UpdateChecker

    private var isPresented: Binding<Bool> {
        Binding(
            get: {
                if case .updateAvailable = updates.status { return true }
                return false
            },
            set: { newValue in
                if !newValue { updates.status = .idle }
            }
        )
    }

    var body: some View {
        Color.clear
            .frame(width: 0, height: 0)
            .alert("Update Available", isPresented: isPresented) {
                if case .updateAvailable(_, let url) = updates.status {
                    Button("Download") { NSWorkspace.shared.open(url) }
                }
                Button("Later", role: .cancel) {}
            } message: {
                if case .updateAvailable(let version, _) = updates.status {
                    Text("Buckett \(version) is available. You are running \(AppVersion.current).")
                }
            }
    }
}

// MARK: - Welcome

struct WelcomeView: View {
    @EnvironmentObject private var appState: AppState

    var body: some View {
        VStack(spacing: 0) {
            Spacer()
            Brand.glyph(size: 84)
                .padding(.bottom, 22)

            Text("Welcome to Buckett")
                .font(.system(size: 34, weight: .bold))
            Text("Start your cloud storage journey — browse and manage\nCloudflare R2 and Backblaze B2 buckets, natively.")
                .font(.title3)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.top, 6)

            HStack(spacing: 14) {
                featureCard(
                    symbol: "arrow.up.arrow.down.circle.fill",
                    title: "Fast Transfers",
                    detail: "Drag & drop uploads with resumable multipart support"
                )
                featureCard(
                    symbol: "lock.shield.fill",
                    title: "Private by Design",
                    detail: "Keys live in your Keychain; requests go straight to your provider"
                )
                featureCard(
                    symbol: "square.grid.2x2.fill",
                    title: "Simple Management",
                    detail: "Previews, batch actions, and per-bucket statistics"
                )
            }
            .padding(.top, 34)
            .padding(.horizontal, 40)
            .frame(maxWidth: 760)

            Button {
                appState.showOnboarding = true
            } label: {
                Text("Add Your First Account")
                    .font(.headline)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 4)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .tint(Brand.indigo)
            .padding(.top, 36)

            Text("Just a few steps to get started")
                .font(.callout)
                .foregroundStyle(.tertiary)
                .padding(.top, 10)
            Spacer()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func featureCard(symbol: String, title: String, detail: String) -> some View {
        VStack(spacing: 10) {
            Image(systemName: symbol)
                .font(.system(size: 26))
                .foregroundStyle(Brand.gradient)
            Text(title)
                .font(.headline)
            Text(detail)
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .frame(height: 128)
        .glassCard(cornerRadius: 14, padding: 14)
    }
}

struct MissingCredentialsView: View {
    var body: some View {
        VStack(spacing: 12) {
            Image(systemName: "key.slash")
                .font(.system(size: 40))
                .foregroundStyle(.secondary)
            Text("Missing credentials")
                .font(.title2.weight(.semibold))
            Text("The secret access key for this account was not found in the Keychain.\nRe-enter it in Settings.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
            SettingsLink {
                Label("Open Settings", systemImage: "gearshape")
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
