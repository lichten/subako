import Foundation

/// images/<tweet_id>_<idx>.<ext> のローカルパス解決 (拡張子不一致に耐える —
/// docs/viewer-features.md §11.2)。
public enum LocalMediaFiles {
    private static let probeExtensions = ["jpg", "png", "webp", "gif", "jpeg"]

    public static func resolve(imagesDir: String, tweetId: String, index: Int, ext: String) -> String? {
        let expected = (imagesDir as NSString).appendingPathComponent("\(tweetId)_\(index).\(ext)")
        if FileManager.default.fileExists(atPath: expected) {
            return expected
        }
        for probe in probeExtensions {
            let candidate = (imagesDir as NSString).appendingPathComponent("\(tweetId)_\(index).\(probe)")
            if FileManager.default.fileExists(atPath: candidate) {
                return candidate
            }
        }
        return nil
    }
}
