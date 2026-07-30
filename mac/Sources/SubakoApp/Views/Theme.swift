import SwiftUI

/// Windows 版の配色 (docs/viewer-features.md §12)。
enum Theme {
    static let sidebarBackground = Color(red: 0xF4 / 255, green: 0xF4 / 255, blue: 0xF6 / 255)
    static let accent = Color(red: 0x1D / 255, green: 0x9B / 255, blue: 0xF0 / 255)   // 未読・リンク
    static let border = Color(red: 0xE1 / 255, green: 0xE8 / 255, blue: 0xED / 255)   // 罫線・タグチップ
    static let retweetGreen = Color(red: 0x00 / 255, green: 0xBA / 255, blue: 0x7C / 255)
    static let auxText = Color(red: 0x66 / 255, green: 0x66 / 255, blue: 0x66 / 255)
    static let auxTextLight = Color(red: 0x88 / 255, green: 0x88 / 255, blue: 0x88 / 255)
}

/// バックグラウンドでデコードした NSImage を actor 間で受け渡すための箱。
/// デコード後に変更しないため安全 (古い SDK では NSImage が Sendable 扱いされない)。
struct ImageBox: @unchecked Sendable {
    let image: NSImage?
    init(_ image: NSImage?) { self.image = image }
}

/// アイコンキャッシュ経由の丸型アイコン画像。
struct CachedIconView: View {
    let url: String?
    let size: CGFloat
    let cache: IconCache?
    /// キャッシュミス時の実 URL 解決 (ニコニコ用)
    var resolveDownloadUrl: (@Sendable (String) async -> String?)?

    @State private var localPath: String?

    var body: some View {
        Group {
            if let localPath, let image = NSImage(contentsOfFile: localPath) {
                Image(nsImage: image)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
            } else {
                Circle().fill(Theme.border)
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
        .task(id: url) {
            localPath = nil
            guard let cache else { return }
            localPath = await cache.localPath(for: url, resolveDownloadUrl: resolveDownloadUrl)
        }
    }
}

/// タグチップ (§4.1)。
struct TagChipView: View {
    let name: String

    var body: some View {
        Text(name)
            .font(.system(size: 10))
            .padding(.horizontal, 5)
            .padding(.vertical, 1)
            .background(Theme.border)
            .clipShape(Capsule())
            .foregroundStyle(Theme.auxText)
    }
}

/// 未読バッジ (件数入り、0 なら非表示)。
struct UnreadBadgeView: View {
    let count: Int64

    var body: some View {
        if count > 0 {
            Text("\(count)")
                .font(.system(size: 10, weight: .semibold))
                .foregroundStyle(.white)
                .padding(.horizontal, 6)
                .padding(.vertical, 1)
                .background(Theme.accent)
                .clipShape(Capsule())
        }
    }
}

/// URL リンク化した本文テキスト (§5.5)。絵文字はシステムフォントがカラー描画する。
struct LinkifiedTextView: View {
    let text: String
    var font: Font = .system(size: 13)

    var body: some View {
        Text(attributed)
            .font(font)
            .textSelection(.enabled)
            .fixedSize(horizontal: false, vertical: true)
    }

    private var attributed: AttributedString {
        var result = AttributedString()
        for segment in Linkifier.split(text) {
            var part = AttributedString(segment.text)
            if segment.isUrl, let url = URL(string: segment.text) {
                part.link = url
                part.foregroundColor = Theme.accent
            }
            result += part
        }
        return result
    }
}

import SubakoCore
