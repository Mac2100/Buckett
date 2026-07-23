import AppKit
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Drop targets (account + bucket)

/// A drop destination that can live in ANY account, encoded for UserDefaults
/// as "accountUUID|bucketName".
struct MenuDropTarget: Hashable {
    let accountID: UUID
    let bucket: String

    var encoded: String { "\(accountID.uuidString)|\(bucket)" }

    init(accountID: UUID, bucket: String) {
        self.accountID = accountID
        self.bucket = bucket
    }

    init?(encoded: String) {
        guard let separator = encoded.firstIndex(of: "|"),
              let uuid = UUID(uuidString: String(encoded[..<separator]))
        else { return nil }
        let name = String(encoded[encoded.index(after: separator)...])
        guard !name.isEmpty else { return nil }
        self.accountID = uuid
        self.bucket = name
    }
}

/// Row shown in the hover drop panel.
struct DropRowModel: Identifiable {
    let target: MenuDropTarget
    let accountName: String
    var id: String { target.encoded }
}

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
    private weak var dropView: StatusDropView?
    private let menu = NSMenu()

    /// Buckets the user checked for the hover drop menu ([String]).
    static let dropBucketsKey = "menuBarDropBuckets"

    static func dropShortlist() -> [String] {
        UserDefaults.standard.stringArray(forKey: dropBucketsKey) ?? []
    }

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
            let view = StatusDropView(frame: button.bounds)
            view.autoresizingMask = [.width, .height]
            button.addSubview(view)
            dropView = view
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
        DropAnimationController.shared.cancelHoverClose()
        let appState = AppState.shared
        var rows: [DropRowModel] = Self.dropShortlist()
            .compactMap { MenuDropTarget(encoded: $0) }
            .filter { appState.isValidDropTarget($0) }
            .compactMap { target in
                guard let account = appState.accountStore.accounts
                    .first(where: { $0.id == target.accountID }) else { return nil }
                let name = account.name.isEmpty ? account.provider.displayName : account.name
                return DropRowModel(target: target, accountName: name)
            }
        if rows.isEmpty, let auto = appState.menuBarAutoTarget() {
            let name = appState.accountStore.accounts
                .first { $0.id == auto.accountID }
                .map { $0.name.isEmpty ? $0.provider.displayName : $0.name } ?? ""
            rows = [DropRowModel(target: auto, accountName: name)]
        }
        DropAnimationController.shared.showHover(rows: rows, near: view)
    }

    /// The drag left the icon — maybe heading into the hover panel, so close
    /// on a grace period that a targeted panel row can cancel.
    func dragExited() {
        DropAnimationController.shared.scheduleHoverClose(after: 0.9)
    }

    func dragEnded() {
        DropAnimationController.shared.scheduleHoverClose(after: 0.4)
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

        let targetItem = NSMenuItem(title: "Drop Menu Buckets", action: nil, keyEquivalent: "")
        let submenu = NSMenu()
        let shortlist = Set(Self.dropShortlist())
        let appState = AppState.shared
        var addedAny = false

        let hint = NSMenuItem(
            title: "Check buckets to show when dragging over the icon",
            action: nil, keyEquivalent: ""
        )
        hint.isEnabled = false
        submenu.addItem(hint)

        for account in appState.accountStore.accounts {
            let buckets = appState.bucketList(for: account.id)
            guard !buckets.isEmpty else { continue }
            submenu.addItem(.separator())
            let displayName = account.name.isEmpty ? account.provider.displayName : account.name
            let header = NSMenuItem(title: displayName.uppercased(), action: nil, keyEquivalent: "")
            header.isEnabled = false
            submenu.addItem(header)
            for bucket in buckets {
                let target = MenuDropTarget(accountID: account.id, bucket: bucket.name)
                let item = NSMenuItem(
                    title: bucket.name, action: #selector(toggleDropBucket(_:)), keyEquivalent: ""
                )
                item.target = self
                item.representedObject = target.encoded
                item.state = shortlist.contains(target.encoded) ? .on : .off
                item.indentationLevel = 1
                submenu.addItem(item)
                addedAny = true
            }
        }
        if !addedAny {
            let none = NSMenuItem(title: "No buckets loaded yet", action: nil, keyEquivalent: "")
            none.isEnabled = false
            submenu.addItem(none)
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

    @objc private func toggleDropBucket(_ sender: NSMenuItem) {
        guard let encoded = sender.representedObject as? String else { return }
        var shortlist = Self.dropShortlist()
        if let index = shortlist.firstIndex(of: encoded) {
            shortlist.remove(at: index)
        } else {
            shortlist.append(encoded)
        }
        UserDefaults.standard.set(shortlist, forKey: Self.dropBucketsKey)
    }

    /// Drop on the icon itself (`target == nil` → automatic target) or on a
    /// specific bucket row inside the hover panel.
    func handleDrop(urls: [URL], target: MenuDropTarget? = nil) {
        DropAnimationController.shared.closeHover()
        guard let resolved = AppState.shared.handleMenuBarDrop(urls: urls, to: target) else {
            return
        }
        DropAnimationController.shared.play(
            fileURL: urls.first,
            count: urls.count,
            bucket: resolved,
            near: dropView
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
        MenuBarController.shared.handleDrop(urls: urls)
        return true
    }

    override func draggingEnded(_ sender: NSDraggingInfo) {
        statusButton?.isHighlighted = false
        MenuBarController.shared.dragEnded()
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
    private var hoverCloseTask: Task<Void, Never>?

    private init() {}

    /// Shown while a drag hovers over the status icon, before the user lets go.
    /// Each listed bucket (possibly across accounts) is its own drop zone.
    func showHover(rows: [DropRowModel], near view: NSView?) {
        guard hoverPanel == nil, !rows.isEmpty else { return }
        let content = BucketDropListView(
            rows: rows,
            theme: ThemeStore.shared.theme,
            onDrop: { target, urls in
                MenuBarController.shared.handleDrop(urls: urls, target: target)
            },
            onTargetedChange: { targeted in
                if targeted {
                    DropAnimationController.shared.cancelHoverClose()
                } else {
                    DropAnimationController.shared.scheduleHoverClose(after: 0.9)
                }
            }
        )
        let height = CGFloat(66 + rows.count * 52)
        hoverPanel = presentPanel(
            rootView: AnyView(content),
            size: NSSize(width: 252, height: height),
            near: view
        )
    }

    func closeHover() {
        hoverCloseTask?.cancel()
        hoverCloseTask = nil
        hoverPanel?.close()
        hoverPanel = nil
    }

    func cancelHoverClose() {
        hoverCloseTask?.cancel()
        hoverCloseTask = nil
    }

    /// Closes the hover panel after a grace period, so a drag can travel from
    /// the icon down into the panel without it vanishing mid-way.
    func scheduleHoverClose(after seconds: TimeInterval) {
        hoverCloseTask?.cancel()
        hoverCloseTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
            guard !Task.isCancelled else { return }
            self?.closeHover()
        }
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

/// Hover state: a list of bucket drop zones. Drop on the icon itself for the
/// automatic target, or move down and drop on a specific bucket.
struct BucketDropListView: View {
    let rows: [DropRowModel]
    let theme: AppTheme
    let onDrop: (MenuDropTarget, [URL]) -> Void
    let onTargetedChange: (Bool) -> Void

    var body: some View {
        VStack(spacing: 6) {
            Text(rows.count == 1 ? "Release to upload" : "Drop on a bucket")
                .font(.callout.weight(.semibold))
            ForEach(rows) { row in
                BucketDropRow(
                    row: row,
                    theme: theme,
                    onDrop: onDrop,
                    onTargetedChange: onTargetedChange
                )
            }
        }
        .padding(12)
        .frame(width: 240)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.1), lineWidth: 1)
        )
        .shadow(color: .black.opacity(0.2), radius: 14, y: 5)
    }
}

struct BucketDropRow: View {
    let row: DropRowModel
    let theme: AppTheme
    let onDrop: (MenuDropTarget, [URL]) -> Void
    let onTargetedChange: (Bool) -> Void

    @State private var targeted = false

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "tray.full.fill")
                .foregroundStyle(targeted ? AnyShapeStyle(theme.gradient) : AnyShapeStyle(.secondary))
            VStack(alignment: .leading, spacing: 0) {
                Text(row.target.bucket)
                    .font(.callout.weight(.medium))
                    .lineLimit(1)
                    .truncationMode(.middle)
                Text(row.accountName)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer()
            Image(systemName: "arrow.down.circle\(targeted ? ".fill" : "")")
                .foregroundStyle(targeted ? theme.primary : Color.secondary)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 9)
        .background(
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .fill(targeted ? theme.primary.opacity(0.18) : Color.primary.opacity(0.045))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .strokeBorder(
                    targeted ? theme.primary.opacity(0.8) : Color.primary.opacity(0.07),
                    lineWidth: targeted ? 1.5 : 1
                )
        )
        .contentShape(RoundedRectangle(cornerRadius: 9))
        .animation(.easeOut(duration: 0.12), value: targeted)
        .onDrop(
            of: [.fileURL],
            isTargeted: Binding(
                get: { targeted },
                set: { newValue in
                    targeted = newValue
                    onTargetedChange(newValue)
                }
            )
        ) { providers in
            let target = row.target
            FileDrop.loadFileURLs(from: providers) { urls in
                guard !urls.isEmpty else { return }
                onDrop(target, urls)
            }
            return true
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
