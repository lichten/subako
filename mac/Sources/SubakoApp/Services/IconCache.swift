import Foundation
import SubakoCore

/// URL 指定の画像のダウンロードとディスクキャッシュ
/// (C# Services/IconCache.cs の移植、docs/viewer-features.md §11.1)。
/// キャッシュ先は data/<subDirectory>/<sha1(url)>.<ext> — 全プラットフォーム共有の派生データ。
actor IconCache {
    private let cacheDir: String
    private var inflight: [String: Task<String?, Never>] = [:]
    private var failed: Set<String> = []   // セッション内ネガティブキャッシュ

    init(dataDir: String, subDirectory: String = "icons") {
        cacheDir = (dataDir as NSString).appendingPathComponent(subDirectory)
        try? FileManager.default.createDirectory(
            atPath: cacheDir, withIntermediateDirectories: true)
    }

    /// この URL のキャッシュ先パス (未取得でも算出できる)。
    nonisolated func cachePath(for url: String) -> String {
        (cacheDir as NSString).appendingPathComponent(IconCacheKey.fileName(for: url))
    }

    /// ローカルキャッシュのパスを返す。未取得ならダウンロード。失敗は nil。
    /// - Parameter resolveDownloadUrl: キャッシュミス時に実際の取得先 URL を解決する処理
    ///   (nil なら url をそのまま使う)。キャッシュのファイル名は url から決まる。
    func localPath(
        for url: String?,
        resolveDownloadUrl: (@Sendable (String) async -> String?)? = nil
    ) async -> String? {
        guard let url, !url.isEmpty, !failed.contains(url) else { return nil }

        let path = cachePath(for: url)
        if FileManager.default.fileExists(atPath: path) {
            return path
        }

        // 同一 URL の並行要求は 1 ダウンロードに束ねる
        if let existing = inflight[url] {
            return await existing.value
        }
        let task = Task<String?, Never> {
            await Self.download(url: url, to: path, resolveDownloadUrl: resolveDownloadUrl)
        }
        inflight[url] = task
        let result = await task.value
        inflight[url] = nil
        if result == nil {
            failed.insert(url)
        }
        return result
    }

    private static func download(
        url: String, to path: String,
        resolveDownloadUrl: (@Sendable (String) async -> String?)?
    ) async -> String? {
        let target: String?
        if let resolveDownloadUrl {
            target = await resolveDownloadUrl(url)
        } else {
            target = url
        }
        guard let target else { return nil }

        // _normal (48px) を _bigger (73px) に置換して取得。404 なら元 URL で再試行
        let bigger = IconCacheKey.biggerVariant(of: target)
        var bytes = await tryGetBytes(bigger)
        if bytes == nil, bigger != target {
            bytes = await tryGetBytes(target)
        }
        guard let bytes else { return nil }

        // 書込は一時ファイル + 原子的リネーム
        let tmp = path + ".tmp"
        do {
            try bytes.write(to: URL(fileURLWithPath: tmp))
            if FileManager.default.fileExists(atPath: path) {
                try? FileManager.default.removeItem(atPath: path)
            }
            try FileManager.default.moveItem(atPath: tmp, toPath: path)
            return path
        } catch {
            try? FileManager.default.removeItem(atPath: tmp)
            return nil
        }
    }

    private static func tryGetBytes(_ url: String) async -> Data? {
        guard let u = URL(string: url) else { return nil }
        var request = URLRequest(url: u, timeoutInterval: 30)
        request.setValue(AppInfo.userAgent, forHTTPHeaderField: "User-Agent")
        guard let (data, response) = try? await URLSession.shared.data(for: request),
              let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode)
        else { return nil }
        return data
    }
}

/// ニコニコ動画のサムネイル URL 解決 (C# Services/NicoThumbnail.cs の移植)。
/// CDN の URL は番号だけからは決定できないため getthumbinfo API で実 URL を引く。
/// 失敗時はサフィックスなしの URL を返して古い動画のケースを救う。
enum NicoThumbnail {
    // getthumbinfo は User-Agent が無いと XML ではなく HTML ページを 200 で返すため必須
    static func resolve(videoNumber: String, fallbackUrl: String) async -> String? {
        guard let url = URL(
            string: "https://ext.nicovideo.jp/api/getthumbinfo/sm\(videoNumber)")
        else { return fallbackUrl }
        var request = URLRequest(url: url, timeoutInterval: 15)
        request.setValue(AppInfo.userAgent, forHTTPHeaderField: "User-Agent")
        if let (data, response) = try? await URLSession.shared.data(for: request),
           let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) {
            let xml = String(decoding: data, as: UTF8.self)
            if let range = xml.range(of: "<thumbnail_url>"),
               let end = xml.range(of: "</thumbnail_url>", range: range.upperBound..<xml.endIndex) {
                return String(xml[range.upperBound..<end.lowerBound])
            }
        }
        return fallbackUrl
    }
}

enum AppInfo {
    static let name = "Subako"
    static let version = "0.1.0"
    static let userAgent = "Subako/0.1"
}
