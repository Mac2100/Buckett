import SwiftUI
import Charts

struct DashboardView: View {
    @EnvironmentObject private var appState: AppState
    @ObservedObject private var aliasStore = BucketAliases.shared
    @AppStorage("sidebarShowAllAccounts") private var showAllAccounts = false

    private var overviewAccounts: [Account] {
        if showAllAccounts {
            return appState.accountStore.accounts
        }
        return appState.selectedAccount.map { [$0] } ?? []
    }

    private var analyzedStats: [BucketStats] {
        overviewAccounts.flatMap { account in
            appState.bucketList(for: account.id).compactMap {
                appState.stats(accountID: account.id, bucket: $0.name)
            }
        }
    }

    private var totalBucketCount: Int {
        overviewAccounts.reduce(0) { $0 + appState.bucketList(for: $1.id).count }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                header
                if !analyzedStats.isEmpty {
                    summaryRow
                }

                ForEach(overviewAccounts) { account in
                    accountSection(account)
                }

                if totalBucketCount == 0 && !appState.bucketsLoading {
                    Text("No buckets found.")
                        .foregroundStyle(.secondary)
                }
            }
            .padding(20)
        }
        .navigationTitle("Dashboard")
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(showAllAccounts ? "All Accounts" : (appState.selectedAccount?.name ?? "Dashboard"))
                .font(.largeTitle.weight(.semibold))
            if showAllAccounts {
                Text("\(overviewAccounts.count) account\(overviewAccounts.count == 1 ? "" : "s") · \(totalBucketCount) bucket\(totalBucketCount == 1 ? "" : "s")")
                    .foregroundStyle(.secondary)
            } else if let account = appState.selectedAccount {
                Text("\(account.provider.displayName) · \(totalBucketCount) bucket\(totalBucketCount == 1 ? "" : "s")")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var summaryRow: some View {
        HStack(spacing: 16) {
            StatTile(
                title: "Analyzed Storage",
                value: analyzedStats.reduce(Int64(0)) { $0 + $1.totalSize }.formattedBytes,
                symbol: "externaldrive.fill"
            )
            StatTile(
                title: "Objects",
                value: "\(analyzedStats.reduce(0) { $0 + $1.objectCount })",
                symbol: "doc.on.doc.fill"
            )
            StatTile(
                title: "Buckets Analyzed",
                value: "\(analyzedStats.count) of \(totalBucketCount)",
                symbol: "tray.2.fill"
            )
        }
    }

    @ViewBuilder
    private func accountSection(_ account: Account) -> some View {
        let buckets = appState.bucketList(for: account.id)

        if showAllAccounts {
            HStack(spacing: 7) {
                Image(systemName: account.provider.symbolName)
                    .foregroundStyle(.secondary)
                Text(account.name.isEmpty ? account.provider.displayName : account.name)
                    .font(.title3.weight(.semibold))
                Text("· \(account.provider.displayName)")
                    .font(.callout)
                    .foregroundStyle(.tertiary)
                Spacer()
            }
            .padding(.top, 6)
        }

        if buckets.isEmpty {
            Text("No buckets in this account.")
                .font(.callout)
                .foregroundStyle(.tertiary)
        } else {
            LazyVGrid(
                columns: [GridItem(.adaptive(minimum: 340, maximum: 520), spacing: 16)],
                alignment: .leading,
                spacing: 16
            ) {
                ForEach(buckets) { bucket in
                    BucketCard(
                        bucket: bucket,
                        displayName: aliasStore.displayName(
                            accountID: account.id, bucket: bucket.name
                        ),
                        stats: appState.stats(accountID: account.id, bucket: bucket.name),
                        analyzing: appState.isAnalyzing(accountID: account.id, bucket: bucket.name)
                    ) {
                        appState.analyze(account: account, bucket: bucket.name)
                    } onOpen: {
                        if appState.selectedAccountID != account.id {
                            appState.selectAccount(account.id)
                        }
                        appState.sidebarSelection = .bucket(bucket.name)
                    }
                }
            }
        }
    }
}

struct StatTile: View {
    let title: String
    let value: String
    let symbol: String

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: symbol)
                .font(.title2)
                .foregroundStyle(.tint)
                .frame(width: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(value).font(.title3.weight(.semibold)).monospacedDigit()
                Text(title).font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
    }
}

struct BucketCard: View {
    let bucket: Bucket
    var displayName: String? = nil
    let stats: BucketStats?
    let analyzing: Bool
    let onAnalyze: () -> Void
    let onOpen: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Label(displayName ?? bucket.name, systemImage: "tray.full")
                    .font(.headline)
                    .lineLimit(1)
                Spacer()
                Button("Browse") { onOpen() }
                    .controlSize(.small)
            }

            if let stats {
                HStack(spacing: 16) {
                    metric(stats.formattedSize, "Total size")
                    metric("\(stats.objectCount)", "Objects")
                    if let newest = stats.newestModified {
                        metric(newest.formatted(.relative(presentation: .named)), "Last change")
                    }
                }

                if !stats.byExtension.isEmpty {
                    Chart(Array(stats.byExtension.prefix(8))) { item in
                        BarMark(
                            x: .value("Size", Double(item.totalSize)),
                            y: .value("Type", item.ext)
                        )
                        .foregroundStyle(by: .value("Type", item.ext))
                        .cornerRadius(3)
                    }
                    .chartLegend(.hidden)
                    .chartXAxis {
                        AxisMarks { value in
                            AxisValueLabel {
                                if let size = value.as(Double.self) {
                                    Text(Int64(size).formattedBytes)
                                }
                            }
                        }
                    }
                    .frame(height: CGFloat(min(stats.byExtension.count, 8)) * 24 + 24)
                }

                HStack {
                    Text("Analyzed \(stats.analyzedAt.formatted(date: .omitted, time: .shortened))")
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                    Spacer()
                    Button("Re-analyze") { onAnalyze() }
                        .controlSize(.small)
                        .disabled(analyzing)
                }
            } else {
                HStack(spacing: 10) {
                    if analyzing {
                        ProgressView().controlSize(.small)
                        Text("Analyzing…").foregroundStyle(.secondary)
                    } else {
                        Text("No usage data yet.")
                            .foregroundStyle(.secondary)
                        Spacer()
                        Button("Analyze") { onAnalyze() }
                            .controlSize(.small)
                    }
                }
                .font(.callout)
            }
        }
        .padding(16)
        .background(
            Color(nsColor: .controlBackgroundColor),
            in: RoundedRectangle(cornerRadius: 12)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 12)
                .strokeBorder(Color.primary.opacity(0.1), lineWidth: 1)
        )
    }

    private func metric(_ value: String, _ label: String) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(value).font(.callout.weight(.semibold)).monospacedDigit()
            Text(label).font(.caption2).foregroundStyle(.secondary)
        }
    }
}
