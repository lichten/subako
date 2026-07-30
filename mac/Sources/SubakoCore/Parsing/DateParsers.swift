import Foundation

/// created_at の解釈 (docs/data-layer.md §2 の 4 形式 + ISO 8601 フォールバック)。
/// C# 実装 (TweetJsonParser.ParseCreatedAt) / Python (fetcher.parse_created_at) と
/// 同じ入力を同じ結果に落とすこと。
public enum DateParsers {
    // "+0000" (基本形) と "+00:00" (コロン入り) の両方を受ける。
    // C# は zzz がコロン必須のため正規化してから試すが、こちらは
    // オフセット書式違いのパターンを列挙して同じ受理範囲にする。
    private static let exactPatterns: [String] = [
        "EEE MMM dd HH:mm:ss xx yyyy",   // Wed Oct 10 20:19:24 +0000 2018
        "EEE MMM dd HH:mm:ss xxx yyyy",
        "yyyy-MM-dd'T'HH:mm:ssxx",
        "yyyy-MM-dd'T'HH:mm:ssxxx",
        "yyyy-MM-dd'T'HH:mm:ss.SSSSSSxx",
        "yyyy-MM-dd'T'HH:mm:ss.SSSSSSxxx",
        "yyyy-MM-dd'T'HH:mm:ss.SSSxx",
        "yyyy-MM-dd'T'HH:mm:ss.SSSxxx",
        "yyyy-MM-dd HH:mm:ssxx",
        "yyyy-MM-dd HH:mm:ssxxx",
    ]

    // オフセットなしのフォールバック (UTC とみなす)。C# の
    // DateTimeOffset.TryParse(..., AssumeUniversal) の実用範囲を再現する。
    private static let assumeUtcPatterns: [String] = [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.SSSSSS",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
    ]

    // DateFormatter は macOS 10.9+ でスレッドセーフ
    private static let exactFormatters: [DateFormatter] =
        exactPatterns.map(makeFormatter)

    private static let assumeUtcFormatters: [DateFormatter] =
        assumeUtcPatterns.map(makeFormatter)

    nonisolated(unsafe) private static let iso8601: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()

    nonisolated(unsafe) private static let iso8601Fractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    /// 保存書式 "yyyy-MM-ddTHH:mm:ssZ" (UTC)。
    private static let utcOutput: DateFormatter = {
        let f = makeFormatter("yyyy-MM-dd'T'HH:mm:ss'Z'")
        return f
    }()

    private static func makeFormatter(_ pattern: String) -> DateFormatter {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.calendar = Calendar(identifier: .gregorian)
        f.dateFormat = pattern
        return f
    }

    public static func parseCreatedAt(_ value: String?) -> Date? {
        guard let value, !value.trimmingCharacters(in: .whitespaces).isEmpty else {
            return nil
        }
        // C# と同じ前処理: "Z" → "+0000" (旧形式には Z が現れないため安全)
        let text = value.trimmingCharacters(in: .whitespaces)
            .replacingOccurrences(of: "Z", with: "+0000")
        for formatter in exactFormatters {
            if let date = formatter.date(from: text) {
                return date
            }
        }
        // ISO 8601 フォールバック
        let original = value.trimmingCharacters(in: .whitespaces)
        if let date = iso8601.date(from: original) ?? iso8601Fractional.date(from: original) {
            return date
        }
        for formatter in assumeUtcFormatters {
            if let date = formatter.date(from: original) {
                return date
            }
        }
        return nil
    }

    /// epoch 秒 (tweets.sort_key)。C# の ToUnixTimeSeconds と同じ床関数。
    public static func unixSeconds(_ date: Date) -> Int64 {
        Int64(date.timeIntervalSince1970.rounded(.down))
    }

    /// DB 保存書式 (docs/viewer-features.md §11.3)。
    public static func utcString(_ date: Date) -> String {
        utcOutput.string(from: date)
    }

    /// UTC の現在時刻を保存書式で返す (users.added_at / read_state.read_at 等)。
    public static func utcNow() -> String {
        utcString(Date())
    }
}
