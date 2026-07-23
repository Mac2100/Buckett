import Foundation
import UserNotifications

/// System (Notification Center) notifications for app events, gated by
/// per-event user preferences (Settings → Notifications).
@MainActor
final class Notifier {
    static let shared = Notifier()

    enum Event: String, CaseIterable {
        case transfersComplete = "notifyTransfersComplete"
        case transferFailed = "notifyTransferFailed"
        case dropStarted = "notifyDropStarted"

        var defaultEnabled: Bool {
            self != .dropStarted
        }
    }

    private var authorizationRequested = false

    private init() {}

    static func isEnabled(_ event: Event) -> Bool {
        if let stored = UserDefaults.standard.object(forKey: event.rawValue) as? Bool {
            return stored
        }
        return event.defaultEnabled
    }

    func post(_ event: Event, title: String, body: String) {
        guard Self.isEnabled(event) else { return }
        // UNUserNotificationCenter requires a real app bundle (not `swift run`).
        guard Bundle.main.bundleIdentifier != nil else { return }

        let center = UNUserNotificationCenter.current()
        if !authorizationRequested {
            authorizationRequested = true
            center.requestAuthorization(options: [.alert, .sound]) { _, _ in }
        }
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        let request = UNNotificationRequest(
            identifier: UUID().uuidString, content: content, trigger: nil
        )
        center.add(request)
    }
}
