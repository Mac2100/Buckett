import AppKit
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Menu bar icon styles

enum MenuBarIconStyle: String, CaseIterable, Identifiable {
    case archive = "archivebox.fill"
    case basket = "basket.fill"
    case tray = "tray.full.fill"
    case cloud = "cloud.fill"
    case drive = "externaldrive.fill"

    var id: String { rawValue }

    var label: String {
        switch self {
        case .archive: return "Archive"
        case .basket: return "Bucket"
        case .tray: return "Tray"
        case .cloud: return "Cloud"
        case .drive: return "Drive"
        }
    }

    static var current: MenuBarIconStyle {
        MenuBarIconStyle(
            rawValue: UserDefaults.standard.string(forKey: "menuBarSymbol") ?? ""
        ) ?? .archive
    }
}

// MARK: - Status item controller

/// Menu bar icon that accepts drag & drop uploads and offers a small menu
/// (open app, pick the drop-target bucket, quit).
@MainActor
final class MenuBarController: NSObject, NSMenuDelegate {
    static let shared = MenuBarController()

    private var statusItem: NSStatusItem?
    private let menu = NSMenu()

    static let targetBucketKey = "menuBarTargetBucket"

    func setup() {
        let stored = UserDefaults.standard.object(forKey: "showMenuBarIcon")
        let visible = (stored as? Bool) ?? true
        setVisible(visible)
    }

    func setVisible(_ visible: Bool) {
        if visible {
            createItemIfNeeded()
        } else if let item = statusItem {
            NSStatusBar.system.removeStatusItem(item)
            statusItem = nil
        }
    }

    private func createItemIfNeeded() {
        guard statusItem == nil else { return }
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = item.button {
            button.image = NSImage(
                systemSymbolName: MenuBarIconStyle.current.rawValue,
                accessibilityDescription: "Buckett — drop files to upload"
            )
            button.toolTip = "Buckett — drop files here to upload"
            let dropView = StatusDropView(frame: button.bounds)
            dropView.autoresizingMask = [.width, .height]
            button.addSubview(dropView)
        }
        menu.delegate = self
        item.menu = menu
        statusItem = item
    }

    /// Re-reads the icon style preference and updates the status button.
    func refreshIcon() {
        statusItem?.button?.image = NSImage(
            systemSymbolName: MenuBarIconStyle.current.rawValue,
            accessibilityDescription: "Buckett — drop files to upload"
        )
    }

    // MARK: Drag hover

    func dragEntered(near view: NSView?) {
        let bucket = AppState.shared.menuBarTargetBucket() ?? "…"
        DropAnimationController.shared.showHover(bucket: bucket, near: view)
    }

    func dragExited() {
        DropAnimationController.shared.closeHover()
    }

    nonisolated func menuNeedsUpdate(_ menu: NSMenu) {
        MainActor.assumeIsolated {
            rebuildMenu()
        }
    }

    private func rebuildMenu() {
        menu.removeAllItems()

        let open = NSMenuItem(title: "Open Buckett", action: #selector(openApp), keyEquivalent: "")
        open.target = self
        menu.addItem(open)
        menu.addItem(.separator())

        let targetItem = NSMenuItem(title: "Drop Target Bucket", action: nil, keyEquivalent: "")
        let submenu = NSMenu()
        let current = UserDefaults.standard.string(forKey: Self.targetBucketKey)
        let buckets = AppState.shared.buckets
        if buckets.isEmpty {
            let none = NSMenuItem(title: "No buckets loaded", action: nil, keyEquivalent: "")
            none.isEnabled = false
            submenu.addItem(none)
        } else {
            let auto = NSMenuItem(
                title: "Selected Bucket (automatic)",
                action: #selector(clearTarget), keyEquivalent: ""
            )
            auto.target = self
            auto.state = current == nil ? .on : .off
            submenu.addItem(auto)
            submenu.addItem(.separator())
            for bucket in buckets {
                let item = NSMenuItem(
                    title: bucket.name, action: #selector(pickTarget(_:)), keyEquivalent: ""
                )
                item.target = self
                item.state = bucket.name == current ? .on : .off
                submenu.addItem(item)
            }
        }
        targetItem.submenu = submenu
        menu.addItem(targetItem)

        let active = AppState.shared.transfers.activeCount
        if active > 0 {
            let status = NSMenuItem(
                title: "\(active) transfer\(active == 1 ? "" : "s") active",
                action: nil, keyEquivalent: ""
            )
            status.isEnabled = false
            menu.addItem(status)
        }

        menu.addItem(.separator())
        let quit = NSMenuItem(
            title: "Quit Buckett",
            action: #selector(NSApplication.terminate(_:)),
            keyEquivalent: "q"
        )
        menu.addItem(quit)
    }

    @objc private func openApp() {
        AppState.shared.openMainWindow()
    }

    @objc private func pickTarget(_ sender: NSMenuItem) {
        UserDefaults.standard.set(sender.title, forKey: Self.targetBucketKey)
    }

    @objc private func clearTarget() {
        UserDefaults.standard.removeObject(forKey: Self.targetBucketKey)
    }

    func handleDrop(urls: [URL], near view: NSView?) {
        DropAnimationController.shared.closeHover()
        guard let bucket = AppState.shared.handleMenuBarDrop(urls: urls) else { return }
        DropAnimationController.shared.play(
            fileURL: urls.first,
            count: urls.count,
            bucket: bucket,
            near: view
        )
    }
}

// MARK: - Drop-receiving view over the status button

final class StatusDropView: NSView {
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    private var statusButton: NSStatusBarButton? {
        superview as? NSStatusBarButton
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        statusButton?.isHighlighted = true
        MenuBarController.shared.dragEntered(near: self)
        return .copy
    }

    override func draggingExited(_ sender: NSDraggingInfo?) {
        statusButton?.isHighlighted = false
        MenuBarController.shared.dragExited()
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        statusButton?.isHighlighted = false
        let urls = sender.draggingPasteboard.readObjects(
            forClasses: [NSURL.self],
            options: [.urlReadingFileURLsOnly: true]
        ) as? [URL] ?? []
        guard !urls.isEmpty else { return false }
        MenuBarController.shared.handleDrop(urls: urls, near: self)
        return true
    }

    // Pass clicks through to the status button so its menu still opens.
    override func mouseDown(with event: NSEvent) {
        statusButton?.performClick(nil)
    }
}

// MARK: - Drop animation

/// Small transient panel under the menu bar icon: the dropped file's icon
/// falls into a bucket, the bucket bounces, and an upload note fades in.
@MainActor
final class DropAnimationController {
    static let shared = DropAnimationController()

    private var panel: NSPanel?
    private var hoverPanel: NSPanel?

    private init() {}

    /// Shown while a drag hovers over the status icon, before the user lets go.
    func showHover(bucket: String, near view: NSView?) {
        guard hoverPanel == nil else { return }
        let content = HoverDropView(bucketName: bucket, theme: ThemeStore.shared.theme)
        hoverPanel = presentPanel(
            rootView: AnyView(content),
            size: NSSize(width: 224, height: 128),
            near: view
        )
    }

    func closeHover() {
        hoverPanel?.close()
        hoverPanel = nil
    }

    func play(fileURL: URL?, count: Int, bucket: String, near view: NSView?) {
        closeHover()
        panel?.close()
        panel = nil

        let icon: NSImage
        if let fileURL {
            icon = NSWorkspace.shared.icon(forFile: fileURL.path)
        } else {
            icon = NSWorkspace.shared.icon(for: .data)
        }

        let content = DropAnimationView(
            fileIcon: icon,
            count: count,
            bucketName: bucket,
            theme: ThemeStore.shared.theme
        )
        let newPanel = presentPanel(
            rootView: AnyView(content),
            size: NSSize(width: 224, height: 148),
            near: view
        )
        panel = newPanel

        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 2_100_000_000)
            guard let self, self.panel === newPanel else { return }
            newPanel.close()
            self.panel = nil
        }
    }

    private func presentPanel(rootView: AnyView, size: NSSize, near view: NSView?) -> NSPanel {
        let hosting = NSHostingController(rootView: rootView)
        let newPanel = NSPanel(contentViewController: hosting)
        newPanel.styleMask = [.borderless, .nonactivatingPanel]
        newPanel.isOpaque = false
        newPanel.backgroundColor = .clear
        newPanel.hasShadow = false
        newPanel.level = .statusBar
        newPanel.isMovable = false
        newPanel.setContentSize(size)

        var origin: NSPoint
        if let view, let window = view.window {
            let frame = window.frame
            origin = NSPoint(x: frame.midX - size.width / 2, y: frame.minY - size.height - 6)
        } else if let screen = NSScreen.main {
            origin = NSPoint(
                x: screen.visibleFrame.midX - size.width / 2,
                y: screen.visibleFrame.maxY - size.height - 10
            )
        } else {
            origin = .zero
        }
        newPanel.setFrameOrigin(origin)
        newPanel.orderFrontRegardless()
        return newPanel
    }
}

/// Hover state: bucket pulses and invites the user to release the drag.
struct HoverDropView: View {
    let bucketName: String
    let theme: AppTheme

    @State private var pulsing = false

    var body: some View {
        VStack(spacing: 9) {
            ZStack {
                RoundedRectangle(cornerRadius: 13, style: .continuous)
                    .fill(theme.gradient)
                    .frame(width: 50, height: 50)
                    .overlay {
                        Image(systemName: MenuBarIconStyle.current.rawValue)
                            .font(.system(size: 24, weight: .semibold))
                            .foregroundStyle(.white)
                    }
                    .scaleEffect(pulsing ? 1.08 : 0.98)

                Image(systemName: "arrow.down")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundStyle(theme.primary)
                    .offset(y: pulsing ? -38 : -44)
            }
            .frame(height: 62)

            VStack(spacing: 1) {
                Text("Release to upload")
                    .font(.callout.weight(.semibold))
                Text("→ \(bucketName)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
        }
        .padding(14)
        .frame(width: 210)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.1), lineWidth: 1)
        )
        .shadow(color: .black.opacity(0.2), radius: 14, y: 5)
        .onAppear {
            withAnimation(.easeInOut(duration: 0.32).repeatForever(autoreverses: true)) {
                pulsing = true
            }
        }
    }
}

struct DropAnimationView: View {
    let fileIcon: NSImage
    let count: Int
    let bucketName: String
    let theme: AppTheme

    @State private var dropped = false
    @State private var squashed = false
    @State private var showLabel = false

    var body: some View {
        VStack(spacing: 10) {
            ZStack {
                RoundedRectangle(cornerRadius: 13, style: .continuous)
                    .fill(theme.gradient)
                    .frame(width: 52, height: 52)
                    .overlay {
                        Image(systemName: MenuBarIconStyle.current.rawValue)
                            .font(.system(size: 25, weight: .semibold))
                            .foregroundStyle(.white)
                    }
                    .scaleEffect(x: squashed ? 1.14 : 1.0, y: squashed ? 0.82 : 1.0)
                    .offset(y: 18)

                Image(nsImage: fileIcon)
                    .resizable()
                    .interpolation(.high)
                    .frame(width: 36, height: 36)
                    .offset(y: dropped ? 14 : -34)
                    .scaleEffect(dropped ? 0.25 : 1)
                    .opacity(dropped ? 0 : 1)

                if count > 1 {
                    Text("+\(count - 1)")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundStyle(.white)
                        .padding(.horizontal, 5)
                        .padding(.vertical, 2)
                        .background(theme.primary, in: Capsule())
                        .offset(x: 26, y: -6)
                        .opacity(showLabel ? 1 : 0)
                }
            }
            .frame(height: 78)

            VStack(spacing: 1) {
                Text("Uploading \(count) file\(count == 1 ? "" : "s")")
                    .font(.callout.weight(.semibold))
                Text("→ \(bucketName)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            .opacity(showLabel ? 1 : 0)
        }
        .padding(14)
        .frame(width: 210)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.1), lineWidth: 1)
        )
        .shadow(color: .black.opacity(0.2), radius: 14, y: 5)
        .onAppear {
            // File falls into the bucket…
            withAnimation(.easeIn(duration: 0.38).delay(0.03)) {
                dropped = true
            }
            // …the bucket squashes on impact and springs back…
            Task {
                try? await Task.sleep(nanoseconds: 400_000_000)
                withAnimation(.easeOut(duration: 0.09)) { squashed = true }
                try? await Task.sleep(nanoseconds: 100_000_000)
                withAnimation(.spring(response: 0.28, dampingFraction: 0.4)) { squashed = false }
            }
            // …and the label fades in.
            withAnimation(.easeOut(duration: 0.25).delay(0.5)) {
                showLabel = true
            }
        }
    }
}
