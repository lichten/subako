import SwiftUI
import SubakoCore

/// タイムライン (無限スクロール §5.6 + スクロール自動既読 §8.1)。
struct TimelineView: View {
    @Bindable var app: AppModel
    let onOpenImage: (TweetItem, ResolvedImage) -> Void

    /// カードごとの表示フレーム (scrollView 座標系)。自動既読の判定に使う
    @State private var cardFrames: [String: CGRect] = [:]
    @State private var visibleRect: CGRect = .zero
    @State private var readTimer: Timer?
    @State private var scrollPosition = ScrollPosition()

    var body: some View {
        ScrollView {
            LazyVStack(spacing: 0) {
                ForEach(app.timelineItems) { item in
                    TweetCardView(app: app, item: item) { image in
                        onOpenImage(item, image)
                    }
                    .onGeometryChange(for: CGRect.self) { proxy in
                        proxy.frame(in: .scrollView)
                    } action: { frame in
                        cardFrames[item.id] = frame
                    }
                }
                if app.timelineLoading {
                    ProgressView()
                        .padding()
                }
            }
            .scrollTargetLayout()
        }
        .scrollPosition($scrollPosition)
        .background(Color(nsColor: .textBackgroundColor))
        .overlay {
            if app.timelineLoadedOnce, app.timelineItems.isEmpty, app.dateFilter.isActive {
                Text("この期間のツイートはありません")
                    .foregroundStyle(Theme.auxTextLight)
            }
        }
        .onScrollGeometryChange(for: ScrollMetrics.self) { geometry in
            ScrollMetrics(
                offsetY: geometry.contentOffset.y,
                contentHeight: geometry.contentSize.height,
                viewportHeight: geometry.containerSize.height)
        } action: { _, metrics in
            visibleRect = CGRect(
                x: 0, y: metrics.offsetY,
                width: 1, height: metrics.viewportHeight)
            // 末尾まで残り約 2 画面分で次ページ (リストが空の間は発火しない — §5.6)
            if !app.timelineItems.isEmpty, !app.timelineLoading, app.timelineHasMore,
               metrics.contentHeight - (metrics.offsetY + metrics.viewportHeight)
                   < metrics.viewportHeight * 2 {
                Task { await app.loadNextTimelinePage() }
            }
            restartReadTimer()
        }
        .onChange(of: app.generation) {
            // 表示切替でリストが入れ替わったら先頭へ (§5.7)
            cardFrames = [:]
            scrollPosition.scrollTo(edge: .top)
        }
        .onChange(of: app.timelineLoadedOnce) {
            // 初期表示分も自動既読の対象 (§8.1)
            if app.timelineLoadedOnce {
                restartReadTimer()
            }
        }
        .onDisappear {
            readTimer?.invalidate()
        }
    }

    private struct ScrollMetrics: Equatable {
        var offsetY: CGFloat
        var contentHeight: CGFloat
        var viewportHeight: CGFloat
    }

    /// スクロールが 300ms 静止したら、ビューポート内に完全表示されているカード
    /// (およびビューポートより背が高く下端を通過したカード) を既読にする (§8.1)。
    private func restartReadTimer() {
        guard !app.isReadOnly else { return }
        readTimer?.invalidate()
        let viewport = visibleRect
        let frames = cardFrames
        readTimer = Timer.scheduledTimer(withTimeInterval: 0.3, repeats: false) { _ in
            Task { @MainActor in
                markVisibleAsRead(viewport: viewport, frames: frames)
            }
        }
    }

    @MainActor
    private func markVisibleAsRead(viewport: CGRect, frames: [String: CGRect]) {
        guard viewport.height > 0 else { return }
        let viewportRange = viewport.minY...(viewport.minY + viewport.height)
        var ids: [String] = []
        for (id, frame) in frames {
            let fullyVisible = frame.minY >= viewportRange.lowerBound
                && frame.maxY <= viewportRange.upperBound
            let tallAndPassed = frame.height > viewport.height
                && frame.maxY <= viewportRange.upperBound
            if fullyVisible || tallAndPassed {
                ids.append(id)
            }
        }
        if !ids.isEmpty {
            app.markRead(itemIds: ids)
        }
    }
}

/// メディアグリッド (§6.1): 3 列の正方形サムネイル。
struct MediaGridView: View {
    @Bindable var app: AppModel
    let onOpen: (MediaItem) -> Void

    var body: some View {
        GeometryReader { geo in
            let cell = max(80, (geo.size.width - 4) / 3)
            ScrollView {
                LazyVGrid(
                    columns: [GridItem(.adaptive(minimum: cell, maximum: cell), spacing: 2)],
                    spacing: 2
                ) {
                    ForEach(app.mediaItems) { item in
                        ThumbCell(item: item, side: cell)
                            .onTapGesture { onOpen(item) }
                            .onAppear {
                                // 末尾近くで次ページ
                                if item.id == app.mediaItems.suffix(24).first?.id {
                                    Task { await app.loadNextMediaPage() }
                                }
                            }
                    }
                }
                if app.mediaLoading {
                    ProgressView().padding()
                }
            }
        }
        .background(Color(nsColor: .textBackgroundColor))
        .overlay {
            if app.mediaLoadedOnce, app.mediaItems.isEmpty, app.dateFilter.isActive {
                Text("この期間のメディアはありません")
                    .foregroundStyle(Theme.auxTextLight)
            }
        }
    }

    private struct ThumbCell: View {
        let item: MediaItem
        let side: CGFloat

        @State private var image: NSImage?

        var body: some View {
            Group {
                if let image {
                    Image(nsImage: image)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                } else {
                    Rectangle().fill(Theme.border)
                }
            }
            .frame(width: side, height: side)
            .clipped()
            .task(id: item.id) {
                let path = item.localPath
                let pixel = side * 2
                image = await Task.detached(priority: .utility) {
                    ThumbImageView.decode(path: path, maxPixel: pixel)
                }.value
            }
        }
    }
}
