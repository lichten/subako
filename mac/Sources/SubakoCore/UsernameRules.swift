import Foundation

/// ユーザー名の検証 (docs/viewer-features.md §9.2)。
/// ASCII 限定 `^[A-Za-z0-9_]+$` — Python 側の規則に揃える
/// (Windows 版は Unicode の文字・数字も通す実装差があり、踏襲しない)。
public enum UsernameRules {
    public static func isValid(_ username: String) -> Bool {
        !username.isEmpty && username.allSatisfy { ch in
            ch.isASCII && (ch.isLetter || ch.isNumber || ch == "_")
        }
    }

    /// 入力の正規化: 前後空白と先頭の `@` を除去。
    public static func normalize(_ input: String) -> String {
        var name = input.trimmingCharacters(in: .whitespacesAndNewlines)
        while name.hasPrefix("@") { name.removeFirst() }
        return name
    }
}
