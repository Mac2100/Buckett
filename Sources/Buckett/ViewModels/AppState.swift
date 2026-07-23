import AppKit
import Combine
import Foundation
import SwiftUI

enum SidebarSelection: Hashable {
    case dashboard
    case bucket(String)
}

@MainActor
final class AppState: ObservableObject {
    static let shared = AppState()

    let accountStore = AccountStore()
    let transfers = TransferManager()
    let updates = UpdateChecker()

    @Published var selectedAccountID: UUID? {
        didSet {
            UserDefaults.standard.set(selectedAccountID?.uuidString, forKey: "selectedAccountID")
        }
    }
    @Published var sidebarSelection: SidebarSelection? = .dashboard
    @Published var showOnboarding = false
    @Published var buckets: [Bucket] = []
    @Published var bucketsLoading = false
    @Published var bucketsError: String?
    @Published var stats: [String: BucketStats] = [:]
    @Published var analyzing: Set<String> = []

    private var clientCache: [UUID: S3Client] = [:]
    private var cancellables: Set<AnyCancellable> = []

    init() {
        // Re-publish nested store changes so views observing AppState refresh
        // when the account list changes.
        accountStore.objectWillChange
            .sink { [weak self] _ in self?.objectWillChange.send() }
            .store(in: &cancellables)

        if let saved = UserDefaults.standard.string(forKey: "selectedAccountID"),
           let uuid = UUID(uuidString: saved),
           accountStore.accounts.contains(where: { $0.id == uuid }) {
            selectedAccountID = uuid
        } else {
            selectedAccountID = accountStore.accounts.first?.id
        }
    }

    var selectedAccount: Account? {
        accountStore.accounts.first { $0.id == selectedAccountID }
    }

    func client(for account: Account) -> S3Client? {
        if let cached = clientCache[account.id] { return cached }
        guard let client = accountStore.client(for: account) else { return nil }
        clientCache[account.id] = client
        return client
    }

    var currentClient: S3Client? {
        selectedAccount.flatMap { client(for: $0) }
    }

    // MARK: - Account lifecycle (invalidates cached clients)

    func saveAccount(_ account: Account, secretKey: String?) {
        accountStore.upsert(account, secretKey: secretKey)
        clientCache[account.id] = nil
        if selectedAccountID == nil {
            selectedAccountID = account.id
        }
        if selectedAccountID == account.id {
            Task { await loadBuckets() }
        }
    }

    func deleteAccount(_ account: Account) {
        accountStore.remove(account)
        clientCache[account.id] = nil
        if selectedAccountID == account.id {
            selectedAccountID = accountStore.accounts.first?.id
            buckets = []
            stats = [:]
            sidebarSelection = .dashboard
            Task { await loadBuckets() }
        }
    }

    func selectAccount(_ id: UUID?) {
        guard id != selectedAccountID else { return }
        selectedAccountID = id
        buckets = []
        stats = [:]
        bucketsError = nil
        sidebarSelection = .dashboard
        Task { await loadBuckets() }
    }

    // MARK: - Buckets

    func loadBuckets() async {
        guard let account = selectedAccount else {
            buckets = []
            return
        }
        guard let client = client(for: account) else {
            bucketsError = "No credentials found for “\(account.name)”. Add the secret key in Settings."
            buckets = []
            return
        }
        bucketsLoading = true
        bucketsError = nil
        do {
            buckets = try await client.listBuckets()
        } catch {
            bucketsError = error.localizedDescription
            buckets = []
        }
        bucketsLoading = false
    }

    func createBucket(named name: String) async throws {
        guard let client = currentClient else { return }
        try await client.createBucket(name)
        await loadBuckets()
    }

    // MARK: - Menu bar drops

    func openMainWindow() {
        NSApp.activate(ignoringOtherApps: true)
        for window in NSApp.windows where window.canBecomeKey {
            window.makeKeyAndOrderFront(nil)
        }
    }

    /// The bucket menu-bar drops currently go to: the explicitly chosen target,
    /// else the selected bucket, else the account's first bucket.
    func menuBarTargetBucket() -> String? {
        if let stored = UserDefaults.standard.string(forKey: MenuBarController.targetBucketKey),
           buckets.contains(where: { $0.name == stored }) {
            return stored
        }
        if case .bucket(let current) = sidebarSelection {
            return current
        }
        return buckets.first?.name
    }

    /// Queues uploads dropped on the menu bar icon. Returns the target bucket
    /// name, or nil (with user-facing feedback) when there is nowhere to upload.
    @discardableResult
    func handleMenuBarDrop(urls: [URL]) -> String? {
        guard let account = selectedAccount, let client = client(for: account) else {
            openMainWindow()
            ToastCenter.shared.show(
                "No account configured",
                detail: "Add an account before dropping files.",
                style: .error
            )
            return nil
        }

        let bucket = menuBarTargetBucket()
        guard let bucket else {
            openMainWindow()
            ToastCenter.shared.show(
                "No bucket available",
                detail: "Create a bucket, then drop files again.",
                style: .error
            )
            return nil
        }

        let transfers = self.transfers
        Task {
            let expanded = await Task.detached(priority: .userInitiated) {
                BrowserModel.expandForUpload(urls: urls)
            }.value
            for (fileURL, suffix) in expanded {
                transfers.enqueueUpload(
                    fileURL: fileURL, bucket: bucket, key: suffix, client: client
                )
            }
        }
        return bucket
    }

    // MARK: - Analytics

    func analyze(bucket: String) {
        guard let client = currentClient, !analyzing.contains(bucket) else { return }
        analyzing.insert(bucket)
        Task {
            defer { analyzing.remove(bucket) }
            do {
                let objects = try await client.listAllObjects(bucket: bucket)
                stats[bucket] = Self.computeStats(bucket: bucket, objects: objects)
            } catch {
                bucketsError = "Analyze failed for \(bucket): \(error.localizedDescription)"
            }
        }
    }

    static func computeStats(bucket: String, objects: [RemoteObject]) -> BucketStats {
        let files = objects.filter { !$0.isFolder }
        var byExt: [String: (count: Int, size: Int64)] = [:]
        for file in files {
            let ext = file.fileExtension.isEmpty ? "(none)" : file.fileExtension
            var entry = byExt[ext] ?? (0, 0)
            entry.count += 1
            entry.size += file.size
            byExt[ext] = entry
        }
        let extStats = byExt
            .map { ExtensionStat(ext: $0.key, count: $0.value.count, totalSize: $0.value.size) }
            .sorted { $0.totalSize > $1.totalSize }
        return BucketStats(
            bucket: bucket,
            objectCount: files.count,
            totalSize: files.reduce(0) { $0 + $1.size },
            byExtension: extStats,
            largestObjects: Array(files.sorted { $0.size > $1.size }.prefix(10)),
            newestModified: files.compactMap(\.lastModified).max(),
            analyzedAt: Date()
        )
    }
}
