import SwiftUI

@main
struct BuckettApp: App {
    @StateObject private var appState = AppState()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(appState)
                .frame(minWidth: 940, minHeight: 560)
                .task {
                    appState.updates.checkOnLaunchIfEnabled()
                    await appState.loadBuckets()
                }
        }
        .commands {
            CommandGroup(after: .appInfo) {
                Button("Check for Updates…") {
                    Task { await appState.updates.check() }
                }
            }
            SidebarCommands()
        }

        Settings {
            SettingsView()
                .environmentObject(appState)
        }
    }
}
