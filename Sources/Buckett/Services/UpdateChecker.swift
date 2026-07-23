import Foundation

/// Checks GitHub Releases for a newer version of Buckett.
@MainActor
final class UpdateChecker: ObservableObject {
    static let repo = "Mac2100/Buckett"
    static let releasesPage = URL(string: "https://github.com/\(repo)/releases/latest")!

    enum Status: Equatable {
        case idle
        case checking
        case upToDate
        case updateAvailable(version: String, url: URL)
        case failed(String)
    }

    @Published var status: Status = .idle
    @Published var lastChecked: Date?

    private struct Release: Decodable {
        var tag_name: String
        var html_url: String
        var assets: [Asset]?

        struct Asset: Decodable {
            var name: String
            var browser_download_url: String
        }
    }

    func checkOnLaunchIfEnabled() {
        guard UserDefaults.standard.object(forKey: "autoCheckUpdates") == nil
                || UserDefaults.standard.bool(forKey: "autoCheckUpdates") else { return }
        Task { await check() }
    }

    func check() async {
        status = .checking
        defer { lastChecked = Date() }
        do {
            var request = URLRequest(
                url: URL(string: "https://api.github.com/repos/\(Self.repo)/releases/latest")!
            )
            request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse else {
                throw URLError(.badServerResponse)
            }
            if http.statusCode == 404 {
                // No releases published yet.
                status = .upToDate
                return
            }
            guard http.statusCode == 200 else {
                throw URLError(.badServerResponse)
            }
            let release = try JSONDecoder().decode(Release.self, from: data)
            let latest = release.tag_name.hasPrefix("v")
                ? String(release.tag_name.dropFirst())
                : release.tag_name

            if Self.isVersion(latest, newerThan: AppVersion.current) {
                let dmg = release.assets?.first { $0.name.hasSuffix(".dmg") }
                let url = dmg.flatMap { URL(string: $0.browser_download_url) }
                    ?? (URL(string: release.html_url) ?? Self.releasesPage)
                status = .updateAvailable(version: latest, url: url)
            } else {
                status = .upToDate
            }
        } catch {
            status = .failed(error.localizedDescription)
        }
    }

    /// Numeric dotted-version comparison ("1.2.10" > "1.2.9").
    static func isVersion(_ a: String, newerThan b: String) -> Bool {
        let av = a.split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 }
        let bv = b.split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 }
        for i in 0..<max(av.count, bv.count) {
            let x = i < av.count ? av[i] : 0
            let y = i < bv.count ? bv[i] : 0
            if x != y { return x > y }
        }
        return false
    }
}
