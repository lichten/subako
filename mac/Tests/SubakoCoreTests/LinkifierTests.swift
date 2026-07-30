import Testing
@testable import SubakoCore

// C# LinkifierTests.cs の移植。

private func reassemble(_ segments: [Linkifier.Segment]) -> String {
    segments.map(\.text).joined()
}

@Suite struct LinkifierSplitTests {
    @Test func noUrl_SingleTextSegment() {
        let segs = Linkifier.split("URL のないテキスト🚀")
        #expect(segs.count == 1)
        #expect(!segs[0].isUrl)
    }

    @Test func urlInMiddle() {
        let text = "詳細は https://example.com/page を見て"
        let segs = Linkifier.split(text)
        #expect(segs.count == 3)
        #expect(segs[1].text == "https://example.com/page")
        #expect(segs[1].isUrl)
        #expect(reassemble(segs) == text)
    }

    @Test func urlAtStartAndEnd() {
        let segs = Linkifier.split("https://a.example/x 中間 http://b.example/y")
        #expect(segs.count == 3)
        #expect(segs[0].isUrl)
        #expect(segs[2].isUrl)
    }

    @Test func trailingJapanesePunctuationExcluded() {
        let text = "これ https://example.com/abc 。次の文"
        let segs = Linkifier.split(text)
        #expect(segs.first(where: \.isUrl)?.text == "https://example.com/abc")
        #expect(reassemble(segs) == text)

        let text2 = "(https://example.com/abc)"
        let segs2 = Linkifier.split(text2)
        #expect(segs2.first(where: \.isUrl)?.text == "https://example.com/abc")
        #expect(reassemble(segs2) == text2)
    }

    @Test func realTweetPattern() {
        let text = "新刊「…」 https://ch.nicovideo.jp/examplech/blomaga/ar1234567 @example_ch #例のチャンネル"
        let segs = Linkifier.split(text)
        #expect(segs.filter(\.isUrl).map(\.text) ==
                ["https://ch.nicovideo.jp/examplech/blomaga/ar1234567"])
        #expect(reassemble(segs) == text)
    }

    @Test func tcoUrlIsLinkified() {
        let segs = Linkifier.split("see https://t.co/u6dBbg4q07")
        #expect(segs.filter(\.isUrl).map(\.text) == ["https://t.co/u6dBbg4q07"])
    }

    @Test func bareSchemeFragmentNotLinkified() {
        let segs = Linkifier.split("切れた https://")
        #expect(!segs.contains(where: { $0.isUrl }))
        #expect(reassemble(segs) == "切れた https://")
    }

    @Test func multipleUrlsPreserveAllText() {
        let text = "a https://x.example/1、b https://x.example/2!c"
        let segs = Linkifier.split(text)
        #expect(segs.filter(\.isUrl).count == 2)
        #expect(reassemble(segs) == text)
    }

    @Test func emptyText_NoSegments() {
        #expect(Linkifier.split("").isEmpty)
    }
}

private let ytThumb = "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg"

@Suite struct ExtractVideoLinksTests {
    @Test(arguments: [
        // YouTube の各種 URL 形式 (Sorsa は t.co を展開済みで本文に入れる)
        ("https://youtu.be/dQw4w9WgXcQ", ytThumb),
        ("https://www.youtube.com/watch?v=dQw4w9WgXcQ", ytThumb),
        ("https://youtube.com/watch?v=dQw4w9WgXcQ", ytThumb),
        ("https://m.youtube.com/watch?v=dQw4w9WgXcQ", ytThumb),
        ("http://www.youtube.com/watch?v=dQw4w9WgXcQ", ytThumb),
        // v= の前後に他のクエリが付く形
        ("https://www.youtube.com/watch?feature=share&v=dQw4w9WgXcQ", ytThumb),
        ("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s", ytThumb),
        ("https://www.youtube.com/shorts/dQw4w9WgXcQ", ytThumb),
        ("https://www.youtube.com/live/dQw4w9WgXcQ", ytThumb),
        ("https://www.youtube.com/embed/dQw4w9WgXcQ", ytThumb),
        ("https://youtu.be/dQw4w9WgXcQ?t=30", ytThumb),
        // ニコニコ動画 (sm/nm/so と短縮 URL)。サムネイルは数字部のみを使う
        ("https://www.nicovideo.jp/watch/sm2455666",
         "https://nicovideo.cdn.nimg.jp/thumbnails/2455666/2455666"),
        ("https://nicovideo.jp/watch/nm10171192",
         "https://nicovideo.cdn.nimg.jp/thumbnails/10171192/10171192"),
        ("https://nico.ms/so12345",
         "https://nicovideo.cdn.nimg.jp/thumbnails/12345/12345"),
        ("https://www.nicovideo.jp/watch/sm9?ref=x",
         "https://nicovideo.cdn.nimg.jp/thumbnails/9/9"),
    ])
    func buildsThumbnailUrl(url: String, expectedThumbnail: String) {
        let links = Linkifier.extractVideoLinks("動画です \(url) どうぞ")
        #expect(links.count == 1)
        #expect(links.first?.pageUrl == url)
        #expect(links.first?.thumbnailUrl == expectedThumbnail)
    }

    @Test(arguments: [
        "https://www.youtube.com/channel/UCabcdefghijklmnop",      // 動画ではない
        "https://www.youtube.com/@somechannel",
        "https://www.youtube.com/watch?v=short",                   // ID が 11 文字未満
        "https://youtu.be/tooLongVideoId123",                      // ID が 11 文字超
        "https://www.nicovideo.jp/user/12345",                     // 動画ではない
        "https://live.nicovideo.jp/watch/lv12345",                 // 生放送は対象外
        "https://example.com/watch?v=dQw4w9WgXcQ",                 // 別ホスト
        "https://notyoutube.com/watch?v=dQw4w9WgXcQ",
    ])
    func ignoresNonVideoUrls(url: String) {
        #expect(Linkifier.extractVideoLinks("見て \(url) ね").isEmpty)
    }

    @Test(arguments: [
        // URL の直後に空白なしで日本語が続くケース (Split の約物除去は末尾が空白のときだけ
        // 効くため、ページ URL は正規表現のマッチ範囲から作る必要がある)
        ("これ https://youtu.be/dQw4w9WgXcQ。おすすめ", "https://youtu.be/dQw4w9WgXcQ"),
        ("これ https://youtu.be/dQw4w9WgXcQ.です", "https://youtu.be/dQw4w9WgXcQ"),
        ("これ https://youtu.be/dQw4w9WgXcQ、と", "https://youtu.be/dQw4w9WgXcQ"),
        ("末尾 https://youtu.be/dQw4w9WgXcQ。", "https://youtu.be/dQw4w9WgXcQ"),
        // クエリ付きは保持する (再生位置を失わない)
        ("時間指定 https://youtu.be/dQw4w9WgXcQ?t=30 から",
         "https://youtu.be/dQw4w9WgXcQ?t=30"),
    ])
    func pageUrlExcludesSurroundingText(text: String, expectedPageUrl: String) {
        let links = Linkifier.extractVideoLinks(text)
        #expect(links.count == 1)
        #expect(links.first?.pageUrl == expectedPageUrl)
        #expect(links.first?.thumbnailUrl == ytThumb)
    }

    @Test func dedupesSameVideoAndKeepsOrder() {
        let links = Linkifier.extractVideoLinks(
            "https://youtu.be/dQw4w9WgXcQ と https://www.youtube.com/watch?v=dQw4w9WgXcQ と "
                + "https://www.nicovideo.jp/watch/sm9")
        #expect(links.count == 2)
        #expect(links[0].thumbnailUrl == ytThumb)
        #expect(links[1].thumbnailUrl == "https://nicovideo.cdn.nimg.jp/thumbnails/9/9")
    }

    @Test func nicoCarriesVideoNumberForApiResolution() {
        // ニコニコの CDN URL は番号だけでは決まらない (新しい動画はサフィックス付き) ので、
        // 取得時に getthumbinfo で解決できるよう番号を持ち回る
        let nico = Linkifier.extractVideoLinks("https://www.nicovideo.jp/watch/sm36810714")
        #expect(nico.count == 1)
        #expect(nico.first?.nicoVideoNumber == "36810714")

        let yt = Linkifier.extractVideoLinks("https://youtu.be/dQw4w9WgXcQ")
        #expect(yt.count == 1)
        #expect(yt.first?.nicoVideoNumber == nil)
    }

    @Test func emptyOrNilText() {
        #expect(Linkifier.extractVideoLinks(nil).isEmpty)
        #expect(Linkifier.extractVideoLinks("").isEmpty)
        #expect(Linkifier.extractVideoLinks("リンクのない本文").isEmpty)
    }
}
