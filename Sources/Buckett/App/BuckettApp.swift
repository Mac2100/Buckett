import SwiftUI

@main
struct BuckettApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var appState = AppState.shared
    @StateObject private var themeStore = ThemeStore.shared

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(appState)
                .environmentObject(themeStore)
                .environment(\.appTheme, themeStore.theme)
                .tint(themeStore.theme.primary)
                .frame(minWidth: 1000, minHeight: 620)
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
                .environmentObject(themeStore)
                .environment(\.appTheme, themeStore.theme)
                .tint(themeStore.theme.primary)
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        ThemeStore.shared.applyAppearance()
        MenuBarController.shared.setup()
    }
}
