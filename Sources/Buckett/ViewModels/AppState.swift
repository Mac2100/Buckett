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
    /// Bucket lists for every account (not just the selected one), so the
    /// menu bar drop menu can offer buckets across accounts.
    @Published var accountBuckets: [UUID: [Bucket]] = [:]
    /// Bucket analytics keyed by "accountUUID|bucket" so every account's
    /// buckets can carry stats simultaneously.
    @Published var stats: [String: BucketStats] = [:]
    @Published var analyzing: Set<String> = []

    static func statsKey(_ accountID: UUID, _ bucket: String) -> String {
        accountID.uuidString + "|" + bucket
    }

    func stats(accountID: UUID, bucket: String) -> BucketStats? {
        stats[Self.statsKey(accountID, bucket)]
    }

    func isAnalyzing(accountID: UUID, bucket: String) -> Bool {
        analyzing.contains(Self.statsKey(accountID, bucket))
    }

    private var clientCache: [UUID: S3Client] = [:]
    private var cancellables: Set<AnyCancellable> = []
    private var statsRefreshTasks: [String: Task<Void, Never>] = [:]

    init() {
        // Re-publish nested store changes so views observing AppState refresh
        // when the account list changes.
        accountStore.objectWillChange
            .sink { [weak self] _ in self?.objectWillChange.send() }
            .store(in: &cancellables)

        // Refresh a bucket's stats shortly after uploads land in it.
        NotificationCenter.default.addObserver(
            forName: .buckettTransferCompleted, object: nil, queue: .main
        ) { [weak self] notification in
            guard let bucket = notification.userInfo?["bucket"] as? String else { return }
            Task { @MainActor [weak self] in
                self?.scheduleStatsRefresh(bucket: bucket)
            }
        }

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
            accountBuckets[account.id] = buckets
            autoAnalyzeAll()
        } catch {
            bucketsError = error.localizedDescription
            buckets = []
        }
        bucketsLoading = false
        loadOtherAccountBuckets()
    }

    /// Background-refreshes bucket lists for accounts other than the selected
    /// one (menu bar drop menu, All Accounts sidebar/overview), then kicks off
    /// their stale-stats analysis.
    private func loadOtherAccountBuckets() {
        for account in accountStore.accounts where account.id != selectedAccountID {
            guard let client = client(for: account) else { continue }
            Task { [weak self] in
                guard let list = try? await client.listBuckets() else { return }
                self?.accountBuckets[account.id] = list
                self?.autoAnalyze(account: account)
            }
        }
    }

    func bucketList(for accountID: UUID) -> [Bucket] {
        if accountID == selectedAccountID, !buckets.isEmpty {
            return buckets
        }
        return accountBuckets[accountID] ?? []
    }

    func isValidDropTarget(_ target: MenuDropTarget) -> Bool {
        bucketList(for: target.accountID).contains { $0.name == target.bucket }
    }

    func createBucket(named name: String) async throws {
        guard let client = currentClient else { return }
        try await client.createBucket(name)
        await loadBuckets()
    }

    /// Deletes a bucket, optionally emptying it first. Emptying purges ALL
    /// object versions and delete markers (Backblaze B2 keeps hidden versions
    /// that make a bucket look empty while still blocking deletion) and aborts
    /// unfinished multipart uploads. Cleans up aliases, stats, and menu bar
    /// drop-target references afterwards.
    func deleteBucket(named name: String, in account: Account, emptyFirst: Bool) async throws {
        guard let client = client(for: account) else {
            throw S3Error(status: 0, code: "NoCredentials", message: "Missing account credentials")
        }
        if emptyFirst {
            let versions = try await client.listAllObjectVersions(bucket: name)
            if !versions.isEmpty {
                try await client.deleteObjectVersions(bucket: name, versions: versions)
            }
            let uploads = (try? await client.listMultipartUploads(bucket: name)) ?? []
            for upload in uploads {
                try? await client.abortMultipartUpload(
                    bucket: name, key: upload.key, uploadID: upload.uploadID
                )
            }
        }
        try await client.deleteBucket(name)

        stats.removeValue(forKey: Self.statsKey(account.id, name))
        BucketAliases.shared.setAlias(nil, accountID: account.id, bucket: name)
        let encoded = MenuDropTarget(accountID: account.id, bucket: name).encoded
        var shortlist = MenuBarController.dropShortlist()
        if shortlist.contains(encoded) {
            shortlist.removeAll { $0 == encoded }
            UserDefaults.standard.set(shortlist, forKey: MenuBarController.dropBucketsKey)
        }
        accountBuckets[account.id]?.removeAll { $0.name == name }
        if account.id == selectedAccountID {
            if sidebarSelection == .bucket(name) {
                sidebarSelection = .dashboard
            }
            await loadBuckets()
        }
    }

    // MARK: - Main window

    /// Captured from the SwiftUI environment so AppKit contexts (Dock reopen,
    /// menu bar "Open Buckett") can recreate the window after it was closed.
    var openWindowAction: OpenWindowAction?

    func openMainWindow() {
        NSApp.activate(ignoringOtherApps: true)
        if let visible = NSApp.windows.first(where: { $0.isVisible && $0.canBecomeKey }) {
            visible.makeKeyAndOrderFront(nil)
        } else {
            openWindowAction?(id: "main")
        }
    }

    // MARK: - Menu bar drops

    /// Where icon-drops go by default: the first checked drop-menu bucket (any
    /// account), else the selected bucket, else the account's first bucket.
    func menuBarAutoTarget() -> MenuDropTarget? {
        if let first = MenuBarController.dropShortlist()
            .compactMap({ MenuDropTarget(encoded: $0) })
            .first(where: { isValidDropTarget($0) }) {
            return first
        }
        guard let selectedID = selectedAccountID else { return nil }
        if case .bucket(let current) = sidebarSelection {
            return MenuDropTarget(accountID: selectedID, bucket: current)
        }
        if let first = buckets.first {
            return MenuDropTarget(accountID: selectedID, bucket: first.name)
        }
        return nil
    }

    /// Queues uploads dropped on the menu bar icon (or one of the hover panel's
    /// bucket rows, via `target` — which may belong to any account). Returns the
    /// resolved target for the animation, or nil (with feedback) when there is
    /// nowhere to upload.
    @discardableResult
    func handleMenuBarDrop(urls: [URL], to target: MenuDropTarget? = nil) -> MenuDropTarget? {
        guard let resolved = target ?? menuBarAutoTarget() else {
            openMainWindow()
            ToastCenter.shared.show(
                "No bucket available",
                detail: "Add an account and create a bucket, then drop files again.",
                style: .error
            )
            return nil
        }
        guard let account = accountStore.accounts.first(where: { $0.id == resolved.accountID }),
              let client = client(for: account) else {
            openMainWindow()
            ToastCenter.shared.show(
                "Missing credentials",
                detail: "Re-enter the secret key for this account in Settings.",
                style: .error
            )
            return nil
        }

        let bucket = resolved.bucket
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
            Notifier.shared.post(
                .dropStarted,
                title: "Upload started",
                body: "\(expanded.count) file\(expanded.count == 1 ? "" : "s") → \(bucket)"
            )
        }
        return resolved
    }

    // MARK: - Analytics

    /// Convenience for the selected account.
    func analyze(bucket: String) {
        guard let account = selectedAccount else { return }
        analyze(account: account, bucket: bucket)
    }

    func analyze(account: Account, bucket: String) {
        Task { await analyzeNow(account: account, bucket: bucket) }
    }

    func analyzeNow(account: Account, bucket: String) async {
        let key = Self.statsKey(account.id, bucket)
        guard let client = client(for: account), !analyzing.contains(key) else { return }
        analyzing.insert(key)
        defer { analyzing.remove(key) }
        do {
            let objects = try await client.listAllObjects(bucket: bucket)
            stats[key] = Self.computeStats(bucket: bucket, objects: objects)
        } catch {
            NSLog("Buckett: analyze failed for \(bucket): \(error.localizedDescription)")
        }
    }

    /// Analyzes the account's buckets that have no stats yet or whose stats
    /// are older than 15 minutes. Sequential so accounts aren't hammered.
    func autoAnalyze(account: Account) {
        let staleBefore = Date().addingTimeInterval(-15 * 60)
        let names = bucketList(for: account.id).map(\.name)
        Task {
            for name in names {
                if let existing = stats[Self.statsKey(account.id, name)],
                   existing.analyzedAt > staleBefore { continue }
                await analyzeNow(account: account, bucket: name)
            }
        }
    }

    func autoAnalyzeAll() {
        guard let account = selectedAccount else { return }
        autoAnalyze(account: account)
    }

    private func scheduleStatsRefresh(bucket: String) {
        // Uploads only carry the bucket name; resolve the owning account,
        // preferring the selected one when names collide across accounts.
        var owner: Account?
        if let selected = selectedAccount,
           bucketList(for: selected.id).contains(where: { $0.name == bucket }) {
            owner = selected
        } else {
            owner = accountStore.accounts.first { account in
                bucketList(for: account.id).contains { $0.name == bucket }
            }
        }
        guard let owner else { return }
        statsRefreshTasks[bucket]?.cancel()
        statsRefreshTasks[bucket] = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 4_000_000_000)
            guard !Task.isCancelled else { return }
            await self?.analyzeNow(account: owner, bucket: bucket)
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
