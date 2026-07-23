import SwiftUI
import Charts

/// Per-bucket statistics: storage overview cards, upload-activity heatmap
/// (local history), file-type breakdown, and largest objects.
struct StatisticsView: View {
    @EnvironmentObject private var appState: AppState
    @Environment(\.appTheme) private var theme
    @ObservedObject private var history = UploadHistory.shared
    let bucket: String

    private var stats: BucketStats? {
        appState.stats[bucket]
    }

    private var analyzing: Bool {
        appState.analyzing.contains(bucket)
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                cardsRow

                HStack(alignment: .top, spacing: 16) {
                    uploadActivityCard
                    recentUploadsCard
                }

                if let stats {
                    HStack(alignment: .top, spacing: 16) {
                        fileTypesCard(stats)
                        largestObjectsCard(stats)
                    }
                    HStack {
                        Text("Analyzed \(stats.analyzedAt.briefFormatted)")
                            .font(.caption)
                            .foregroundStyle(.tertiary)
                        Button("Re-analyze") {
                            appState.analyze(bucket: bucket)
                        }
                        .controlSize(.small)
                        .disabled(analyzing)
                        if analyzing {
                            ProgressView().controlSize(.small)
                        }
                    }
                }
            }
            .padding(18)
        }
    }

    // MARK: - Overview cards

    private var cardsRow: some View {
        HStack(spacing: 14) {
            statCard(
                title: "Storage",
                value: stats?.formattedSize ?? "—",
                caption: stats.map { "Total \($0.objectCount) files" } ?? "Run analyze",
                symbol: "internaldrive.fill",
                color: theme.secondary
            )
            statCard(
                title: "Objects",
                value: stats.map { "\($0.objectCount)" } ?? "—",
                caption: stats.map { "\($0.byExtension.count) file types" } ?? "Run analyze",
                symbol: "doc.on.doc.fill",
                color: theme.primary
            )
            statCard(
                title: "Est. Monthly Cost",
                value: stats.map { costString(bytes: $0.totalSize) } ?? "—",
                caption: "Storage only, after free tier",
                symbol: "dollarsign.circle.fill",
                color: .green
            )
            statCard(
                title: "Last Change",
                value: stats?.newestModified.map {
                    $0.formatted(.relative(presentation: .named))
                } ?? "—",
                caption: stats?.newestModified.map { $0.briefFormatted } ?? "Run analyze",
                symbol: "clock.fill",
                color: .orange
            )
        }
        .overlay {
            if stats == nil {
                analyzePrompt
            }
        }
    }

    private var analyzePrompt: some View {
        VStack(spacing: 8) {
            Text("No usage data yet")
                .font(.callout.weight(.semibold))
            Button {
                appState.analyze(bucket: bucket)
            } label: {
                if analyzing {
                    ProgressView().controlSize(.small)
                } else {
                    Label("Analyze Bucket", systemImage: "chart.bar.doc.horizontal")
                }
            }
            .buttonStyle(.borderedProminent)
            .tint(theme.primary)
            .disabled(analyzing)
        }
        .padding(14)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .shadow(color: .black.opacity(0.15), radius: 12, y: 4)
    }

    private func statCard(
        title: String, value: String, caption: String, symbol: String, color: Color
    ) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(title)
                    .font(.caption.weight(.medium))
                    .foregroundStyle(.secondary)
                Spacer()
                Image(systemName: symbol)
                    .font(.system(size: 17))
                    .foregroundStyle(color)
            }
            Text(value)
                .font(.system(size: 24, weight: .bold))
                .monospacedDigit()
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            Text(caption)
                .font(.caption2)
                .foregroundStyle(.tertiary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(cornerRadius: 13, padding: 14)
    }

    private func costString(bytes: Int64) -> String {
        let gb = Double(bytes) / 1_073_741_824
        let freeGB = 10.0
        let ratePerGB: Double
        switch appState.selectedAccount?.provider {
        case .backblazeB2: ratePerGB = 0.006
        default: ratePerGB = 0.015 // Cloudflare R2
        }
        let cost = max(0, gb - freeGB) * ratePerGB
        return String(format: "$%.2f", cost)
    }

    // MARK: - Upload activity (local history)

    private var uploadActivityCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Upload Activity")
                    .font(.headline)
                Spacer()
                let recent = history.recentDays(30)
                let activeDays = recent.filter { ($0.day?.files ?? 0) > 0 }.count
                Text("Active days: \(activeDays)/30")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Text("Files uploaded from this Mac, last 5 weeks")
                .font(.caption)
                .foregroundStyle(.tertiary)

            let days = history.recentDays(35)
            LazyVGrid(
                columns: Array(repeating: GridItem(.flexible(), spacing: 4), count: 7),
                spacing: 4
            ) {
                ForEach(Array(days.enumerated()), id: \.offset) { _, entry in
                    RoundedRectangle(cornerRadius: 3)
                        .fill(heatColor(bytes: entry.day?.bytes ?? 0))
                        .frame(height: 16)
                        .help(
                            "\(entry.date.formatted(date: .abbreviated, time: .omitted)): "
                            + "\(entry.day?.files ?? 0) files, \((entry.day?.bytes ?? 0).formattedBytes)"
                        )
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(cornerRadius: 13, padding: 16)
    }

    private func heatColor(bytes: Int64) -> Color {
        guard bytes > 0 else { return Color.primary.opacity(0.06) }
        let mb = Double(bytes) / 1_048_576
        let intensity = min(1, 0.25 + log10(max(1, mb)) / 4)
        return theme.secondary.opacity(intensity)
    }

    private var recentUploadsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Recent Uploads")
                .font(.headline)
            Text("Last 7 days, from this Mac")
                .font(.caption)
                .foregroundStyle(.tertiary)

            let days = Array(history.recentDays(7).reversed())
            let maxBytes = max(1, days.map { $0.day?.bytes ?? 0 }.max() ?? 1)
            VStack(spacing: 7) {
                ForEach(Array(days.enumerated()), id: \.offset) { index, entry in
                    HStack(spacing: 10) {
                        Text(dayLabel(entry.date, index: index))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .frame(width: 78, alignment: .leading)
                        Text("\(entry.day?.files ?? 0) files")
                            .font(.caption)
                            .frame(width: 52, alignment: .leading)
                        Text((entry.day?.bytes ?? 0).formattedBytes)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .frame(width: 64, alignment: .leading)
                        GeometryReader { proxy in
                            Capsule()
                                .fill(Color.primary.opacity(0.07))
                                .overlay(alignment: .leading) {
                                    Capsule()
                                        .fill(theme.gradient)
                                        .frame(
                                            width: proxy.size.width
                                                * CGFloat(Double(entry.day?.bytes ?? 0) / Double(maxBytes))
                                        )
                                }
                        }
                        .frame(height: 7)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(cornerRadius: 13, padding: 16)
    }

    private func dayLabel(_ date: Date, index: Int) -> String {
        if index == 0 { return "Today" }
        if index == 1 { return "Yesterday" }
        return date.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day())
    }

    // MARK: - File types & largest objects

    private func fileTypesCard(_ stats: BucketStats) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Storage by File Type")
                .font(.headline)
            if stats.byExtension.isEmpty {
                Text("No files").font(.caption).foregroundStyle(.tertiary)
            } else {
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
                .frame(height: CGFloat(min(stats.byExtension.count, 8)) * 26 + 26)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(cornerRadius: 13, padding: 16)
    }

    private func largestObjectsCard(_ stats: BucketStats) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Largest Objects")
                .font(.headline)
            if stats.largestObjects.isEmpty {
                Text("No files").font(.caption).foregroundStyle(.tertiary)
            } else {
                VStack(spacing: 6) {
                    ForEach(stats.largestObjects.prefix(8)) { object in
                        HStack(spacing: 8) {
                            Image(systemName: object.symbolName)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                                .frame(width: 15)
                            Text(object.key)
                                .font(.caption)
                                .lineLimit(1)
                                .truncationMode(.middle)
                            Spacer()
                            Text(object.formattedSize)
                                .font(.caption.weight(.medium))
                                .monospacedDigit()
                        }
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(cornerRadius: 13, padding: 16)
    }
}
