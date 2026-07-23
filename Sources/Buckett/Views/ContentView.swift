import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var appState: AppState

    var body: some View {
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 200, ideal: 240)
        } detail: {
            detail
        }
        .background(UpdateAlertHost(updates: appState.updates))
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
                    BrowserView(bucket: name, client: client, transfers: appState.transfers)
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

/// Presents an alert when a newer release is found.
private struct UpdateAlertHost: View {
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

struct WelcomeView: View {
    var body: some View {
        VStack(spacing: 14) {
            Image(systemName: "tray.2.fill")
                .font(.system(size: 56))
                .foregroundStyle(.tint)
            Text("Welcome to Buckett")
                .font(.largeTitle.weight(.semibold))
            Text("A visual bucket explorer for Cloudflare R2 and Backblaze B2.\nAdd an account to get started.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
            SettingsLink {
                Label("Add Account…", systemImage: "plus.circle.fill")
            }
            .controlSize(.large)
            .buttonStyle(.borderedProminent)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
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
