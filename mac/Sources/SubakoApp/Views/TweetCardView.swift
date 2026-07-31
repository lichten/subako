import SwiftUI
import SubakoCore

/// ツイートカード (§5.1)。
struct TweetCardView: View {
    @Bindable var app: AppModel
    let item: TweetItem
    let onOpenImage: (ResolvedImage) -> Void

    @Environment(\.openURL) private var openURL
    /// 画像の表示倍率 (セッション限り — リスト再構築で等倍に戻る)
    @State private var imageScales: [String: ImageScale] = [:]

    var body: some View {
        HStack(alignment: .top, spacing: 0) {
            // 未読アクセントバー (§5.1-1)
            Rectangle()
                .fill(item.row.isRead ? Color.clear : Theme.accent)
                .frame(width: 3)
            HStack(alignment: .top, spacing: 8) {
                CachedIconView(url: item.headerIconUrl, size: 40, cache: app.iconCache)
                    .padding(.top, 2)
                VStack(alignment: .leading, spacing: 4) {
                    if let rtBy = item.retweetedByName {
                        Text("\(rtBy) さんがリツイート")
                            .font(.system(size: 11))
                            .foregroundStyle(Theme.retweetGreen)
                    }
                    header
                    if let reply = item.replyHeader {
                        Text(reply)
                            .font(.system(size: 11))
                            .foregroundStyle(Theme.accent)
                    }
                    LinkifiedTextView(text: item.displayText)
                    if !item.bodyImages.isEmpty {
                        imagesFlow(item.bodyImages)
                    }
                    videoThumbnails(item.xVideos.map {
                        VideoThumb(pageUrl: $0.pageUrl, thumbnailUrl: $0.thumbnailUrl, nico: nil)
                    } + item.videoLinks.map {
                        VideoThumb(pageUrl: $0.pageUrl, thumbnailUrl: $0.thumbnailUrl, nico: $0.nicoVideoNumber)
                    }, width: 320, height: 180)
                    if item.hasQuote {
                        quoteBlock
                    }
                    Text(item.countsText)
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.auxTextLight)
                }
                Spacer(minLength: 0)
            }
            .padding(10)
        }
        .background(Color(nsColor: .textBackgroundColor))
        .overlay(alignment: .bottom) {
            Rectangle().fill(Theme.border).frame(height: 1)
        }
        .contextMenu {
            Button("ブラウザで開く") {
                openURL(item.statusURL)
            }
            if let author = item.addableAuthor {
                Button("@\(author) をユーザーに追加") {
                    Task {
                        do {
                            let (added, name) = try await app.addAuthorFromTweet(item)
                            app.statusMessage = added
                                ? "@\(name) を追加しました" : "@\(name) は登録済みです"
                        } catch {
                            app.statusMessage = error.localizedDescription
                        }
                    }
                }
                .disabled(app.isReadOnly)
            }
            Button(item.row.isRead ? "未読にする" : "既読にする") {
                app.toggleRead(itemId: item.id)
            }
            .disabled(app.isReadOnly)
        }
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            Text(item.headerDisplayName)
                .font(.system(size: 13, weight: .bold))
                .lineLimit(1)
            if let username = item.headerUsername {
                Text("@\(username)")
                    .font(.system(size: 12))
                    .foregroundStyle(Theme.auxTextLight)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            Text(item.localDateText)
                .font(.system(size: 11))
                .foregroundStyle(Theme.auxTextLight)
        }
    }

    // MARK: - 画像 (§5.3 折り返し配置 + 倍率メニュー)

    /// 既定倍率 (設定値)。settings.json の値が既知の倍率でなければ等倍に落とす
    private var defaultScale: ImageScale {
        ImageScale(rawValue: app.settings.defaultImageScale) ?? .normal
    }

    private func imagesFlow(_ images: [ResolvedImage]) -> some View {
        FlowLayout(spacing: 6) {
            ForEach(images) { image in
                let effective = imageScales[image.id] ?? defaultScale
                ThumbImageView(
                    path: image.localPath,
                    maxWidth: image.baseWidth * effective.rawValue,
                    maxHeight: image.baseHeight * effective.rawValue)
                    .onTapGesture { onOpenImage(image) }
                    .contextMenu {
                        ForEach(ImageScale.allCases) { s in
                            Toggle(s.label, isOn: Binding(
                                get: { effective == s },
                                set: { _ in imageScales[image.id] = s }))
                        }
                        Divider()
                        // Mac 版独自: 既定倍率の永続化 (mac/README.md 既知差分)
                        Button("このサイズを既定にする") {
                            app.settings.defaultImageScale = effective.rawValue
                            app.settings.save()
                        }
                        .disabled(effective == defaultScale)
                    }
            }
        }
    }

    // MARK: - 動画サムネイル (§5.4)

    struct VideoThumb: Identifiable {
        let pageUrl: String
        let thumbnailUrl: String
        let nico: String?
        var id: String { thumbnailUrl }
    }

    @ViewBuilder
    private func videoThumbnails(_ thumbs: [VideoThumb], width: CGFloat, height: CGFloat) -> some View {
        // 表示順: X 添付動画 → 本文リンク由来。同一サムネイルは先勝ち 1 件
        let unique = thumbs.reduce(into: [VideoThumb]()) { acc, t in
            if !acc.contains(where: { $0.thumbnailUrl == t.thumbnailUrl }) {
                acc.append(t)
            }
        }
        if !unique.isEmpty {
            FlowLayout(spacing: 6) {
                ForEach(unique) { thumb in
                    VideoThumbnailView(
                        thumb: thumb, width: width, height: height,
                        cache: app.thumbnailCache)
                }
            }
        }
    }

    // MARK: - 引用ブロック (§5.1-9)

    private var quoteBlock: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 4) {
                CachedIconView(url: item.row.quotedIconUrl, size: 20, cache: app.iconCache)
                Text(item.row.quotedDisplayName ?? item.row.quotedUsername ?? "")
                    .font(.system(size: 12, weight: .semibold))
                    .lineLimit(1)
                if let name = item.row.quotedUsername {
                    Text("@\(name)")
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.auxTextLight)
                        .lineLimit(1)
                }
            }
            if let text = item.row.quotedText, !text.isEmpty {
                LinkifiedTextView(text: text, font: .system(size: 12))
            }
            if !item.quotedImages.isEmpty {
                imagesFlow(item.quotedImages)
            }
            videoThumbnails(item.quotedVideoLinks.map {
                VideoThumb(pageUrl: $0.pageUrl, thumbnailUrl: $0.thumbnailUrl, nico: $0.nicoVideoNumber)
            }, width: 240, height: 135)
        }
        .padding(8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(Theme.border, lineWidth: 1))
    }
}

/// ローカルファイルのサムネイル画像 (縮小デコードでメモリを抑える)。
struct ThumbImageView: View {
    let path: String
    let maxWidth: CGFloat
    let maxHeight: CGFloat

    @State private var image: NSImage?

    var body: some View {
        Group {
            if let image {
                Image(nsImage: image)
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(maxWidth: maxWidth, maxHeight: maxHeight)
                    .clipShape(RoundedRectangle(cornerRadius: 6))
            } else {
                RoundedRectangle(cornerRadius: 6)
                    .fill(Theme.border)
                    .frame(width: min(maxWidth, 120), height: min(maxHeight, 84))
            }
        }
        .task(id: "\(path)|\(Int(maxWidth))") {
            let p = path
            // 拡大時はデコード画素も増やす (§5.3、上限 2048px)
            let decodeWidth = min(maxWidth * 2, 2048)
            image = await Task.detached(priority: .utility) {
                ImageBox(Self.decode(path: p, maxPixel: decodeWidth))
            }.value.image
        }
    }

    nonisolated static func decode(path: String, maxPixel: CGFloat) -> NSImage? {
        guard let source = CGImageSourceCreateWithURL(
            URL(fileURLWithPath: path) as CFURL, nil)
        else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceThumbnailMaxPixelSize: Int(maxPixel),
            kCGImageSourceCreateThumbnailWithTransform: true,
        ]
        guard let cg = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary)
        else { return nil }
        return NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
    }
}

/// 動画サムネイル (クリックで動画ページをブラウザで開く。取得失敗は枠ごと出さない — §5.4)。
struct VideoThumbnailView: View {
    let thumb: TweetCardView.VideoThumb
    let width: CGFloat
    let height: CGFloat
    let cache: IconCache?

    @Environment(\.openURL) private var openURL
    @State private var localPath: String?

    var body: some View {
        Group {
            if let localPath, let image = NSImage(contentsOfFile: localPath) {
                ZStack {
                    Image(nsImage: image)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                        .frame(width: width, height: height)
                        .clipShape(RoundedRectangle(cornerRadius: 6))
                    Image(systemName: "play.circle.fill")
                        .font(.system(size: 36))
                        .foregroundStyle(.white.opacity(0.85))
                        .shadow(radius: 3)
                }
                .onTapGesture {
                    if let url = URL(string: thumb.pageUrl) {
                        openURL(url)
                    }
                }
            }
        }
        .task(id: thumb.thumbnailUrl) {
            guard let cache else { return }
            if let nico = thumb.nico {
                // ニコニコはキャッシュミス時のみ getthumbinfo で実 URL を解決 (§3.6)
                localPath = await cache.localPath(
                    for: thumb.thumbnailUrl,
                    resolveDownloadUrl: { url in
                        await NicoThumbnail.resolve(videoNumber: nico, fallbackUrl: url)
                    })
            } else {
                localPath = await cache.localPath(for: thumb.thumbnailUrl)
            }
        }
    }
}
