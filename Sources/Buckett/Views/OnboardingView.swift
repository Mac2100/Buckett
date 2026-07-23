import SwiftUI

/// Guided account-setup wizard, modeled on R2Client's stepper flow but adapted
/// to Buckett's S3-only feature set: Provider → Connection → Credentials → Finish.
struct OnboardingView: View {
    @EnvironmentObject private var appState: AppState
    @Environment(\.dismiss) private var dismiss
    @Environment(\.appTheme) private var theme

    enum Step: Int, CaseIterable {
        case provider, connection, credentials, finish

        var title: String {
            switch self {
            case .provider: return "Provider"
            case .connection: return "Connection"
            case .credentials: return "Credentials"
            case .finish: return "Finish"
            }
        }

        var symbol: String {
            switch self {
            case .provider: return "cloud"
            case .connection: return "link"
            case .credentials: return "key"
            case .finish: return "checkmark.seal"
            }
        }
    }

    @State private var step: Step = .provider
    @State private var name = ""
    @State private var provider: Provider = .cloudflareR2
    @State private var cloudflareAccountID = ""
    @State private var b2Region = ""
    @State private var customEndpoint = ""
    @State private var accessKeyID = ""
    @State private var secretKey = ""
    @State private var testing = false
    @State private var testResult: TestResult?

    enum TestResult: Equatable {
        case success(Int)
        case failure(String)
    }

    var body: some View {
        VStack(spacing: 0) {
            stepper
                .padding(.top, 24)
                .padding(.bottom, 18)
                .padding(.horizontal, 28)

            Divider().opacity(0.4)

            ScrollView {
                content
                    .padding(28)
                    .frame(maxWidth: 460)
            }
            .frame(maxWidth: .infinity)

            Divider().opacity(0.4)
            footer
                .padding(.horizontal, 24)
                .padding(.vertical, 14)
        }
        .frame(width: 620, height: 540)
        .background(.regularMaterial)
    }

    // MARK: - Stepper header

    private var stepper: some View {
        HStack(spacing: 0) {
            ForEach(Step.allCases, id: \.rawValue) { s in
                if s != .provider {
                    Rectangle()
                        .fill(
                            s.rawValue <= step.rawValue
                                ? AnyShapeStyle(theme.gradient)
                                : AnyShapeStyle(Color.primary.opacity(0.12))
                        )
                        .frame(height: 2)
                        .frame(maxWidth: .infinity)
                        .padding(.horizontal, 6)
                        .padding(.bottom, 22)
                }
                VStack(spacing: 6) {
                    ZStack {
                        Circle()
                            .fill(
                                s.rawValue <= step.rawValue
                                    ? AnyShapeStyle(theme.gradient)
                                    : AnyShapeStyle(Color.primary.opacity(0.08))
                            )
                            .frame(width: 34, height: 34)
                        Image(systemName: s.rawValue < step.rawValue ? "checkmark" : s.symbol)
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundStyle(s.rawValue <= step.rawValue ? .white : .secondary)
                    }
                    Text(s.title)
                        .font(.caption)
                        .foregroundStyle(s == step ? .primary : .secondary)
                }
            }
        }
        .animation(.snappy(duration: 0.2), value: step)
    }

    // MARK: - Step content

    @ViewBuilder
    private var content: some View {
        switch step {
        case .provider: providerStep
        case .connection: connectionStep
        case .credentials: credentialsStep
        case .finish: finishStep
        }
    }

    private var providerStep: some View {
        VStack(alignment: .leading, spacing: 16) {
            stepHeading(
                "Choose your storage provider",
                "Both are S3-compatible — Buckett talks to them directly."
            )

            HStack(spacing: 12) {
                ForEach(Provider.allCases) { p in
                    providerCard(p)
                }
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("Display name").font(.callout.weight(.medium))
                TextField(provider.displayName, text: $name)
                    .textFieldStyle(.roundedBorder)
            }
            .padding(.top, 6)
        }
    }

    private func providerCard(_ p: Provider) -> some View {
        Button {
            withAnimation(.snappy(duration: 0.15)) { provider = p }
        } label: {
            VStack(spacing: 10) {
                Image(systemName: p.symbolName)
                    .font(.system(size: 26))
                    .foregroundStyle(provider == p ? AnyShapeStyle(theme.gradient) : AnyShapeStyle(.secondary))
                Text(p.displayName)
                    .font(.callout.weight(.medium))
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 22)
            .background(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(provider == p ? theme.primary.opacity(0.10) : Color.primary.opacity(0.04))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(
                        provider == p ? theme.primary.opacity(0.7) : Color.primary.opacity(0.08),
                        lineWidth: provider == p ? 1.5 : 1
                    )
            )
            .contentShape(RoundedRectangle(cornerRadius: 12))
        }
        .buttonStyle(.plain)
    }

    private var connectionStep: some View {
        VStack(alignment: .leading, spacing: 16) {
            switch provider {
            case .cloudflareR2:
                stepHeading(
                    "Cloudflare account ID",
                    "Shown on the R2 overview page and in your dashboard URL."
                )
                labeledField("Account ID", text: $cloudflareAccountID, prompt: "32-character hex ID")
            case .backblazeB2:
                stepHeading(
                    "Backblaze region",
                    "The part after “s3.” in your bucket's S3 endpoint."
                )
                labeledField("Region", text: $b2Region, prompt: "e.g. us-west-004")
            }

            labeledField(
                "Custom endpoint (optional)",
                text: $customEndpoint,
                prompt: "Overrides the derived endpoint"
            )

            if let endpoint = draft.endpointURL {
                HStack(spacing: 7) {
                    Image(systemName: "link")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Text(endpoint.absoluteString)
                        .font(.caption.monospaced())
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
                .padding(10)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 8))
            }
        }
    }

    private var credentialsStep: some View {
        VStack(alignment: .leading, spacing: 16) {
            stepHeading(
                "API credentials",
                provider == .cloudflareR2
                    ? "Create an R2 API token with Admin Read & Write so Buckett can list your buckets."
                    : "Create an App Key in the Backblaze console; the keyID is your Access Key ID."
            )

            labeledField("Access Key ID", text: $accessKeyID, prompt: "")

            VStack(alignment: .leading, spacing: 6) {
                Text("Secret Access Key").font(.callout.weight(.medium))
                SecureField("", text: $secretKey)
                    .textFieldStyle(.roundedBorder)
            }

            Link(destination: provider.consoleURL) {
                Label("Open \(provider.shortName) console to create a key", systemImage: "arrow.up.right.square")
                    .font(.callout)
            }

            Label(
                "Stored only in your macOS Keychain, used solely to sign requests sent directly to \(provider.displayName).",
                systemImage: "lock.shield"
            )
            .font(.caption)
            .foregroundStyle(.secondary)
        }
    }

    private var finishStep: some View {
        VStack(spacing: 18) {
            theme.glyph(size: 56)
            Text(name.isEmpty ? provider.displayName : name)
                .font(.title3.weight(.semibold))
            Text("Run a quick connection test, then you're done.")
                .foregroundStyle(.secondary)

            Button {
                testConnection()
            } label: {
                if testing {
                    ProgressView().controlSize(.small)
                        .frame(minWidth: 110)
                } else {
                    Text("Test Connection")
                        .frame(minWidth: 110)
                }
            }
            .controlSize(.large)
            .disabled(testing)

            switch testResult {
            case .success(let count):
                Label(
                    "Connected — \(count) bucket\(count == 1 ? "" : "s") visible",
                    systemImage: "checkmark.circle.fill"
                )
                .foregroundStyle(.green)
            case .failure(let message):
                VStack(spacing: 4) {
                    Label(message, systemImage: "xmark.circle.fill")
                        .foregroundStyle(.red)
                        .multilineTextAlignment(.center)
                    if message.contains("AccessDenied") && provider == .cloudflareR2 {
                        Text("Tip: listing buckets requires an R2 token with Admin Read & Write permission.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            case nil:
                EmptyView()
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 10)
    }

    // MARK: - Footer

    private var footer: some View {
        HStack {
            Button("Cancel") { dismiss() }
                .keyboardShortcut(.cancelAction)
            Spacer()
            if step != .provider {
                Button("Previous") {
                    withAnimation(.snappy(duration: 0.2)) {
                        step = Step(rawValue: step.rawValue - 1) ?? .provider
                    }
                }
            }
            if step != .finish {
                Button("Next") {
                    withAnimation(.snappy(duration: 0.2)) {
                        step = Step(rawValue: step.rawValue + 1) ?? .finish
                    }
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(!canAdvance)
            } else {
                Button("Add Account") {
                    save()
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(!draft.isConfigured || secretKey.isEmpty)
            }
        }
    }

    // MARK: - Helpers

    private func stepHeading(_ title: String, _ subtitle: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).font(.title3.weight(.semibold))
            Text(subtitle).font(.callout).foregroundStyle(.secondary)
        }
    }

    private func labeledField(_ label: String, text: Binding<String>, prompt: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label).font(.callout.weight(.medium))
            TextField(prompt, text: text)
                .textFieldStyle(.roundedBorder)
                .autocorrectionDisabled()
        }
    }

    private var draft: Account {
        var account = Account()
        account.name = name.isEmpty ? provider.displayName : name
        account.provider = provider
        account.cloudflareAccountID = cloudflareAccountID.trimmingCharacters(in: .whitespaces)
        account.b2Region = b2Region.trimmingCharacters(in: .whitespaces)
        account.customEndpoint = customEndpoint.trimmingCharacters(in: .whitespaces)
        account.accessKeyID = accessKeyID.trimmingCharacters(in: .whitespaces)
        return account
    }

    private var canAdvance: Bool {
        switch step {
        case .provider:
            return true
        case .connection:
            return draft.endpointURL != nil
        case .credentials:
            return !draft.accessKeyID.isEmpty && !secretKey.isEmpty
        case .finish:
            return true
        }
    }

    private func testConnection() {
        guard let client = S3Client(account: draft, secretKey: secretKey) else {
            testResult = .failure("Endpoint is not configured")
            return
        }
        testing = true
        testResult = nil
        Task {
            do {
                let buckets = try await client.listBuckets()
                testResult = .success(buckets.count)
            } catch {
                testResult = .failure(error.localizedDescription)
            }
            testing = false
        }
    }

    private func save() {
        let account = draft
        appState.saveAccount(account, secretKey: secretKey)
        appState.selectAccount(account.id)
        dismiss()
        ToastCenter.shared.show("Account added", detail: account.name)
    }
}
