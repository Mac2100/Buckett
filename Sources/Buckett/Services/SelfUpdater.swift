import AppKit
import Foundation

/// In-place self-update: downloads the release DMG, mounts it, swaps the app
/// bundle at its current location (with rollback on failure), and relaunches.
/// No Sparkle dependency; works with the ad-hoc-signed GitHub release builds.
@MainActor
final class SelfUpdater: ObservableObject {
    static let shared = SelfUpdater()

    enum Phase: Equatable {
        case idle
        case downloading
        case installing
        case relaunching
        case failed(String)
    }

    @Published var phase: Phase = .idle

    var isBusy: Bool {
        phase == .downloading || phase == .installing || phase == .relaunching
    }

    private init() {}

    /// Kicks off download + install + relaunch. Falls back to opening the URL
    /// in the browser when it isn't a DMG asset.
    func install(from url: URL) {
        guard !isBusy else { return }
        guard url.path.hasSuffix(".dmg") else {
            NSWorkspace.shared.open(url)
            return
        }
        phase = .downloading
        ToastCenter.shared.show("Downloading update…", style: .info)
        Task {
            do {
                let (tempFile, response) = try await URLSession.shared.download(from: url)
                guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                    throw SelfUpdateError("Download failed (HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)). If the repository is private, download the DMG from GitHub instead.")
                }
                let dmg = FileManager.default.temporaryDirectory
                    .appendingPathComponent("Buckett-update-\(UUID().uuidString).dmg")
                try? FileManager.default.removeItem(at: dmg)
                try FileManager.default.moveItem(at: tempFile, to: dmg)

                phase = .installing
                ToastCenter.shared.show("Installing update…", style: .info)
                let target = Bundle.main.bundleURL
                try await Task.detached(priority: .userInitiated) {
                    try SelfUpdater.performInstall(dmg: dmg, target: target)
                }.value

                phase = .relaunching
                ToastCenter.shared.show("Relaunching…", style: .info)
                relaunch(target: target)
            } catch {
                phase = .failed(error.localizedDescription)
                ToastCenter.shared.show(
                    "Update failed", detail: error.localizedDescription, style: .error
                )
            }
        }
    }

    // MARK: - Install mechanics (off main thread)

    private nonisolated static func run(_ tool: String, _ arguments: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        let stderr = Pipe()
        process.standardOutput = Pipe()
        process.standardError = stderr
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            let message = String(
                data: stderr.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8
            ) ?? ""
            throw SelfUpdateError(
                "\((tool as NSString).lastPathComponent) failed: \(message.prefix(200))"
            )
        }
    }

    private nonisolated static func performInstall(dmg: URL, target: URL) throws {
        let fm = FileManager.default
        let mount = fm.temporaryDirectory
            .appendingPathComponent("buckett-mount-\(UUID().uuidString)", isDirectory: true)

        try run("/usr/bin/hdiutil", [
            "attach", dmg.path, "-mountpoint", mount.path, "-nobrowse", "-readonly", "-quiet"
        ])
        defer {
            try? run("/usr/bin/hdiutil", ["detach", mount.path, "-quiet", "-force"])
            try? fm.removeItem(at: dmg)
        }

        let appName = target.lastPathComponent
        var source = mount.appendingPathComponent(appName)
        if !fm.fileExists(atPath: source.path) {
            let contents = (try? fm.contentsOfDirectory(
                at: mount, includingPropertiesForKeys: nil
            )) ?? []
            guard let anyApp = contents.first(where: { $0.pathExtension == "app" }) else {
                throw SelfUpdateError("No app found inside the update image.")
            }
            source = anyApp
        }

        // Stage a copy outside the (read-only) image before touching the target.
        let stagedDirectory = fm.temporaryDirectory
            .appendingPathComponent("buckett-staged-\(UUID().uuidString)", isDirectory: true)
        try fm.createDirectory(at: stagedDirectory, withIntermediateDirectories: true)
        let staged = stagedDirectory.appendingPathComponent(appName)
        try run("/usr/bin/ditto", [source.path, staged.path])

        guard fm.isWritableFile(atPath: target.deletingLastPathComponent().path) else {
            throw SelfUpdateError(
                "Cannot write to \(target.deletingLastPathComponent().path). Move Buckett to a writable location (like /Applications) and try again."
            )
        }

        // Move the running app aside, then the new version into place; roll back on failure.
        let parked = fm.temporaryDirectory
            .appendingPathComponent("buckett-old-\(UUID().uuidString).app")
        try fm.moveItem(at: target, to: parked)
        do {
            do {
                try fm.moveItem(at: staged, to: target)
            } catch {
                // Cross-volume move can fail; fall back to a copy.
                try run("/usr/bin/ditto", [staged.path, target.path])
            }
        } catch {
            try? fm.moveItem(at: parked, to: target)
            throw error
        }
        try? fm.removeItem(at: parked)
        try? fm.removeItem(at: stagedDirectory)
    }

    private func relaunch(target: URL) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = ["-c", "sleep 0.6; /usr/bin/open \"\(target.path)\""]
        try? process.run()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
            NSApp.terminate(nil)
        }
    }
}

struct SelfUpdateError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}
