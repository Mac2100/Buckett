import SwiftUI

enum TransferFilter: String, CaseIterable, Identifiable {
    case all, queued, active, completed, failed
    var id: String { rawValue }

    var label: String {
        switch self {
        case .all: return "All"
        case .queued: return "Queued"
        case .active: return "Active"
        case .completed: return "Completed"
        case .failed: return "Failed"
        }
    }

    func matches(_ state: TransferState) -> Bool {
        switch self {
        case .all: return true
        case .queued: return state == .queued
        case .active: return state == .running
        case .completed: return state == .completed
        case .failed:
            if case .failed = state { return true }
            return state == .cancelled
        }
    }
}

struct TransfersView: View {
    @ObservedObject var transfers: TransferManager
    @State private var filter: TransferFilter = .all

    private var filtered: [TransferTask] {
        transfers.tasks.filter { filter.matches($0.state) }
    }

    var body: some View {
        VStack(spacing: 0) {
            controls
            Divider().opacity(0.4)

            if filtered.isEmpty {
                emptyState
            } else {
                ScrollView {
                    LazyVStack(spacing: 8) {
                        ForEach(filtered) { task in
                            TransferRowCard(task: task, transfers: transfers)
                        }
                    }
                    .padding(16)
                    .frame(maxWidth: 760)
                }
                .frame(maxWidth: .infinity)
            }
        }
    }

    private var controls: some View {
        HStack(spacing: 8) {
            ForEach(TransferFilter.allCases) { f in
                Button {
                    withAnimation(.snappy(duration: 0.15)) { filter = f }
                } label: {
                    HStack(spacing: 5) {
                        Text(f.label)
                            .font(.system(size: 12, weight: .medium))
                        if f == .all && !transfers.tasks.isEmpty {
                            Text("\(transfers.tasks.count)")
                                .font(.system(size: 10, weight: .bold))
                                .padding(.horizontal, 5)
                                .padding(.vertical, 1)
                                .background(Color.primary.opacity(0.1), in: Capsule())
                        }
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 5)
                    .background(
                        Capsule().fill(
                            filter == f ? AnyShapeStyle(.background) : AnyShapeStyle(Color.clear)
                        )
                    )
                    .overlay(
                        Capsule().strokeBorder(
                            filter == f ? Color.primary.opacity(0.12) : Color.clear, lineWidth: 1
                        )
                    )
                    .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .foregroundStyle(filter == f ? .primary : .secondary)
            }
            Spacer()
            Button("Clear Completed") {
                transfers.clearFinished()
            }
            .disabled(!transfers.tasks.contains { $0.state.isFinished })
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 9)
    }

    private var emptyState: some View {
        VStack(spacing: 10) {
            Spacer()
            Image(systemName: "arrow.up.arrow.down.circle")
                .font(.system(size: 38))
                .foregroundStyle(.tertiary)
            Text(filter == .all ? "No transfers yet" : "No \(filter.label.lowercased()) transfers")
                .font(.title3.weight(.medium))
                .foregroundStyle(.secondary)
            Text("Uploads and downloads appear here with live progress.")
                .font(.callout)
                .foregroundStyle(.tertiary)
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }
}

// MARK: - Row

struct TransferRowCard: View {
    @ObservedObject var task: TransferTask
    let transfers: TransferManager
    @Environment(\.appTheme) private var theme

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: task.symbolName)
                .font(.title3)
                .foregroundStyle(task.kind == .upload ? theme.primary : theme.secondary)
                .frame(width: 24)

            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Text(task.displayName)
                        .font(.callout.weight(.medium))
                        .lineLimit(1)
                        .truncationMode(.middle)
                    if case .completed = task.state {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 12))
                            .foregroundStyle(.green)
                    }
                    Spacer()
                    Text(task.totalBytes.formattedBytes)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                }

                switch task.state {
                case .queued:
                    Text("Queued").font(.caption).foregroundStyle(.secondary)
                case .running:
                    ProgressView(value: task.fractionCompleted)
                        .progressViewStyle(.linear)
                        .controlSize(.small)
                        .tint(task.kind == .upload ? theme.primary : theme.secondary)
                    HStack(spacing: 10) {
                        Text("\(Int(task.fractionCompleted * 100))%")
                        Text("\(task.transferredBytes.formattedBytes) / \(task.totalBytes.formattedBytes)")
                        if task.bytesPerSecond > 1 {
                            Text("\(Int64(task.bytesPerSecond).formattedBytes)/s")
                        }
                        if let (part, total) = task.partProgress {
                            Text("Parts \(part) / \(total)")
                        }
                    }
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
                case .completed:
                    Text(task.kind == .upload ? "Uploaded to \(task.bucket)/\(task.key)" : "Saved to \(task.localURL.path)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                case .cancelled:
                    Text("Cancelled — retrying resumes multipart uploads")
                        .font(.caption)
                        .foregroundStyle(.orange)
                case .failed(let message):
                    Text(message)
                        .font(.caption)
                        .foregroundStyle(.red)
                        .lineLimit(2)
                }
            }

            actions
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 11)
        .background(
            Color(nsColor: .controlBackgroundColor),
            in: RoundedRectangle(cornerRadius: 11, style: .continuous)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 11, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.07), lineWidth: 1)
        )
    }

    @ViewBuilder
    private var actions: some View {
        HStack(spacing: 6) {
            switch task.state {
            case .queued, .running:
                iconButton("xmark.circle.fill", help: "Cancel") {
                    transfers.cancel(task)
                }
            case .failed, .cancelled:
                iconButton("arrow.clockwise.circle.fill", help: "Retry") {
                    transfers.retry(task)
                }
            case .completed:
                if task.kind == .upload {
                    iconButton("link.circle.fill", help: "Copy share link (7 days)") {
                        if let url = task.client.presignedURL(
                            bucket: task.bucket, key: task.key, expires: 7 * 24 * 3600
                        ) {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.setString(url.absoluteString, forType: .string)
                            ToastCenter.shared.show("Share link copied", detail: "Valid for 7 days")
                        }
                    }
                } else {
                    iconButton("magnifyingglass.circle.fill", help: "Show in Finder") {
                        NSWorkspace.shared.activateFileViewerSelecting([task.localURL])
                    }
                }
            }
        }
    }

    private func iconButton(_ symbol: String, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 16))
                .foregroundStyle(.secondary)
        }
        .buttonStyle(.plain)
        .help(help)
    }
}
