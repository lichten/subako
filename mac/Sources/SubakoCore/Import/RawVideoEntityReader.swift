import Foundation

/// X 添付動画の表示時抽出 (C# Data/RawVideoEntityReader.cs の移植)。
/// tweet_media には載せない (§3 の idx は Python と共有の契約) ため、
/// tweets.raw_offset / raw_length で tweets.jsonl の該当行をシーク再読みして
/// entities の type: video / animated_gif を拾う。
/// 行に video.twimg.com を含むときだけ JSON パースする (追加コストは動画ツイート分のみ)。
public enum RawVideoEntityReader {
    public static func read(jsonlPath: String, rawOffset: Int64, rawLength: Int64) -> [VideoEntity] {
        guard rawLength > 0,
              let handle = FileHandle(forReadingAtPath: jsonlPath)
        else { return [] }
        defer { try? handle.close() }
        do {
            try handle.seek(toOffset: UInt64(rawOffset))
            guard let data = try handle.read(upToCount: Int(rawLength)),
                  data.count == Int(rawLength)
            else { return [] }
            let line = String(decoding: data, as: UTF8.self)
            guard line.contains("video.twimg.com"),
                  let root = JSONValue.parseLine(line)
            else { return [] }
            return TweetJsonParser.extractVideoEntities(root)
        } catch {
            return []
        }
    }
}
