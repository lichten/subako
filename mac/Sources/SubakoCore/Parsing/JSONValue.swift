import Foundation

/// tweets.jsonl の行を欠損耐性つきで読むための動的 JSON 表現。
/// C# 実装 (System.Text.Json の JsonElement) と同じアクセス規則を提供する:
/// 型が合わないメンバーは「無い」として扱う (docs/data-layer.md §2 の best effort)。
public enum JSONValue: Equatable, Sendable {
    case object([String: JSONValue])
    case array([JSONValue])
    case string(String)
    case integer(Int64)
    case number(Double)
    case bool(Bool)
    case null
}

extension JSONValue: Decodable {
    private struct StringKey: CodingKey {
        let stringValue: String
        let intValue: Int? = nil
        init(stringValue: String) { self.stringValue = stringValue }
        init?(intValue: Int) { return nil }
    }

    public init(from decoder: any Decoder) throws {
        if let keyed = try? decoder.container(keyedBy: StringKey.self) {
            var dict = [String: JSONValue](minimumCapacity: keyed.allKeys.count)
            for key in keyed.allKeys {
                dict[key.stringValue] = try keyed.decode(JSONValue.self, forKey: key)
            }
            self = .object(dict)
        } else if var unkeyed = try? decoder.unkeyedContainer() {
            var items = [JSONValue]()
            while !unkeyed.isAtEnd {
                items.append(try unkeyed.decode(JSONValue.self))
            }
            self = .array(items)
        } else {
            let single = try decoder.singleValueContainer()
            if single.decodeNil() {
                self = .null
            } else if let b = try? single.decode(Bool.self) {
                self = .bool(b)
            } else if let i = try? single.decode(Int64.self) {
                self = .integer(i)
            } else if let d = try? single.decode(Double.self) {
                self = .number(d)
            } else if let s = try? single.decode(String.self) {
                self = .string(s)
            } else {
                throw DecodingError.dataCorrupted(DecodingError.Context(
                    codingPath: decoder.codingPath,
                    debugDescription: "unsupported JSON value"))
            }
        }
    }
}

extension JSONValue {
    /// 1 行 JSON をパースする。壊れた行は nil。
    public static func parseLine(_ line: String) -> JSONValue? {
        guard let data = line.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(JSONValue.self, from: data)
    }

    public var isObject: Bool {
        if case .object = self { return true }
        return false
    }

    /// オブジェクトのメンバー。オブジェクトでなければ nil。
    public func member(_ key: String) -> JSONValue? {
        guard case .object(let dict) = self else { return nil }
        return dict[key]
    }

    /// メンバーが文字列のときのみ値を返す (C# の GetString と同規則)。
    public func string(_ key: String) -> String? {
        guard case .string(let s) = member(key) else { return nil }
        return s
    }

    /// メンバーがオブジェクトのときのみ返す (C# の GetObject と同規則)。
    public func object(_ key: String) -> JSONValue? {
        guard let v = member(key), v.isObject else { return nil }
        return v
    }

    /// メンバーが JSON の true のときのみ true (数値の 1 等は false)。
    public func boolIsTrue(_ key: String) -> Bool {
        if case .bool(true) = member(key) { return true }
        return false
    }

    /// メンバーが整数のときのみ値を返し、それ以外は 0
    /// (C# の TryGetInt64 は小数を拒否するため .number は 0 に落とす)。
    public func int64(_ key: String) -> Int64 {
        if case .integer(let n) = member(key) { return n }
        return 0
    }
}
