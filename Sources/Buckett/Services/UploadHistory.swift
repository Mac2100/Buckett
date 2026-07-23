import Foundation

/// Local record of upload activity per day, powering the Statistics heatmap.
/// Purely local — nothing is sent anywhere.
@MainActor
final class UploadHistory: ObservableObject {
    static let shared = UploadHistory()

    struct Day: Codable {
        var files: Int = 0
        var bytes: Int64 = 0
    }

    /// Keyed by "yyyy-MM-dd" (local time zone).
    @Published private(set) var days: [String: Day] = [:]

    private var fileURL: URL {
        AccountStore.supportDirectory.appendingPathComponent("upload-history.json")
    }

    private static let keyFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        f.locale = Locale(identifier: "en_US_POSIX")
        return f
    }()

    private init() {
        if let data = try? Data(contentsOf: fileURL),
           let decoded = try? JSONDecoder().decode([String: Day].self, from: data) {
            days = decoded
        }
    }

    static func key(for date: Date) -> String {
        keyFormatter.string(from: date)
    }

    func record(bytes: Int64, date: Date = Date()) {
        let key = Self.key(for: date)
        var day = days[key] ?? Day()
        day.files += 1
        day.bytes += bytes
        days[key] = day
        save()
    }

    func day(for date: Date) -> Day? {
        days[Self.key(for: date)]
    }

    /// The last `count` days, oldest first.
    func recentDays(_ count: Int) -> [(date: Date, day: Day?)] {
        let calendar = Calendar.current
        let today = calendar.startOfDay(for: Date())
        return (0..<count).reversed().compactMap { offset in
            guard let date = calendar.date(byAdding: .day, value: -offset, to: today) else {
                return nil
            }
            return (date, day(for: date))
        }
    }

    private func save() {
        try? FileManager.default.createDirectory(
            at: AccountStore.supportDirectory, withIntermediateDirectories: true
        )
        if let data = try? JSONEncoder().encode(days) {
            try? data.write(to: fileURL, options: .atomic)
        }
    }
}
