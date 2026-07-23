import SwiftUI

struct TransfersView: View {
    @ObservedObject var transfers: TransferManager

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("Transfers").font(.headline)
                Spacer()
                Button("Clear Finished") {
                    transfers.clearFinished()
                }
                .controlSize(.small)
                .disabled(!transfers.tasks.contains { $0.state.isFinished })
            }
            .padding(12)
            Divider()

            if transfers.tasks.isEmpty {
                VStack(spacing: 8) {
                    Image(systemName: "arrow.up.arrow.down.circle")
                        .font(.system(size: 30))
                        .foregroundStyle(.tertiary)
                    Text("No transfers").foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, minHeight: 140)
            } else {
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(transfers.tasks) { task in
                            TransferRow(task: task, transfers: transfers)
                            Divider()
                        }
                    }
                }
                .frame(maxHeight: 320)
            }
        }
        .frame(width: 380)
    }
}

struct TransferRow: View {
    @ObservedObject var task: TransferTask
    let transfers: TransferManager

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: task.symbolName)
                .font(.title3)
                .foregroundStyle(task.kind == .upload ? Color.blue : Color.green)

            VStack(alignment: .leading, spacing: 3) {
                Text(task.displayName)
                    .font(.callout)
                    .lineLimit(1)
                    .truncationMode(.middle)

                switch task.state {
                case .queued:
                    Text("Queued").font(.caption).foregroundStyle(.secondary)
                case .running:
                    ProgressView(value: task.fractionCompleted)
                        .progressViewStyle(.linear)
                        .controlSize(.small)
                    Text("\(task.transferredBytes.formattedBytes) of \(task.totalBytes.formattedBytes)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                case .completed:
                    Text("Done · \(task.totalBytes.formattedBytes)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                case .cancelled:
                    Text("Cancelled").font(.caption).foregroundStyle(.orange)
                case .failed(let message):
                    Text(message)
                        .font(.caption)
                        .foregroundStyle(.red)
                        .lineLimit(2)
                }
            }
            Spacer()

            switch task.state {
            case .queued, .running:
                Button {
                    transfers.cancel(task)
                } label: {
                    Image(systemName: "xmark.circle.fill")
                }
                .buttonStyle(.borderless)
                .help("Cancel")
            case .failed, .cancelled:
                Button {
                    transfers.retry(task)
                } label: {
                    Image(systemName: "arrow.clockwise.circle.fill")
                }
                .buttonStyle(.borderless)
                .help(task.kind == .upload ? "Retry (resumes multipart uploads)" : "Retry")
            case .completed:
                Image(systemName: "checkmark.circle.fill")
                    .foregroundStyle(.green)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }
}
