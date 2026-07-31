import SwiftUI
import SubakoCore

/// onScrollGeometryChange から受け取る計測値。値の受け渡し専用で、
/// **@State には入れない** (下の ScrollTracker の注意を参照)。
struct ScrollMetrics: Equatable {
    var offsetY: CGFloat = 0
    var contentHeight: CGFloat = 0
    var viewportHeight: CGFloat = 0
}

/// スクロール中に変わる作業用の状態。無限スクロール (§5.6)・自動既読 (§8.1)・
/// キーボードスクロール (§5.7) で共用する。
///
/// **@State に直接置いてはいけない。** スクロールのたびに値が変わるため、
/// 書くたびにビューの body が再評価され、項目が数万件あるリストでは
/// レイアウトが破綻する (実測でスクロール 1 段につき body 4 回)。
/// 参照型の箱に入れて、SwiftUI に変更を伝えずに読み書きする。
@MainActor
final class ScrollTracker {
    // 直近の計測値
    var offsetY: CGFloat = 0
    var contentHeight: CGFloat = 0
    var viewportHeight: CGFloat = 0
    /// カードごとの表示フレーム (scrollView 座標系 = ビューポート相対)。自動既読の判定に使う
    var cardFrames: [String: CGRect] = [:]
    /// スクロール静止を待つタイマー (§8.1)
    var readTimer: Timer?
    /// 直前に指示した目標位置。キーリピート中は計測値の更新がキーの速さに
    /// 追いつかず、同じ位置から同じ目標を計算し直してしまうため、
    /// 反映待ちの間はこちらを基準にする
    var pendingTarget: CGFloat?
    var pendingRelease: Task<Void, Never>?

    func update(from metrics: ScrollMetrics) {
        offsetY = metrics.offsetY
        contentHeight = metrics.contentHeight
        viewportHeight = metrics.viewportHeight
    }
}

extension View {
    /// キーボードスクロール (§5.7)。
    /// `refocusOn` が変わるたびにフォーカスを取り戻す (サイドバーをクリックすると
    /// フォーカスを奪われ、以後キーが効かなくなるため)。
    func keyboardScroll<Trigger: Equatable>(
        tracker: ScrollTracker,
        position: Binding<ScrollPosition>,
        refocusOn refocusTrigger: Trigger
    ) -> some View {
        modifier(KeyboardScrollModifier(
            tracker: tracker, position: position, refocusTrigger: refocusTrigger))
    }
}

/// キー割り当ては macOS 標準と主要ブラウザ (Safari / Chrome / Firefox) に合わせる:
///
/// | キー | 動作 |
/// |---|---|
/// | ↑ / ↓ | 48px |
/// | PageUp / PageDown (ノート型は fn+↑/↓) | 1 画面ぶん (数行残す) |
/// | Home / End (ノート型は fn+←/→) | 先頭 / 末尾 |
/// | Space / Shift+Space | PageDown / PageUp と同量 |
/// | Command+↑ / Command+↓ | 先頭 / 末尾 |
///
/// 移動量の規則は Windows 版と共通 (SubakoCore.KeyboardScroll)。
private struct KeyboardScrollModifier<Trigger: Equatable>: ViewModifier {
    let tracker: ScrollTracker
    @Binding var position: ScrollPosition
    let refocusTrigger: Trigger

    @FocusState private var focused: Bool

    private static var handledKeys: Set<KeyEquivalent> {
        [.upArrow, .downArrow, .pageUp, .pageDown, .home, .end, .space]
    }

    func body(content: Content) -> some View {
        content
            .focusable()
            .focusEffectDisabled()
            .focused($focused)
            .defaultFocus($focused, true)
            .task {
                // onAppear 直後はまだレスポンダチェーンに乗っていないことがある
                try? await Task.sleep(for: .milliseconds(150))
                focused = true
            }
            .onChange(of: refocusTrigger) {
                focused = true
                // 表示切替で先頭に戻るので、持ち越した目標は捨てる
                tracker.pendingRelease?.cancel()
                tracker.pendingTarget = nil
            }
            // 既定では .down しか拾わないので、押しっぱなしのために .repeat も要る
            .onKeyPress(keys: Self.handledKeys, phases: [.down, .repeat]) { press in
                guard let command = Self.command(for: press) else { return .ignored }
                let target = KeyboardScroll.nextOffset(
                    command, offsetY: tracker.pendingTarget ?? tracker.offsetY,
                    viewportHeight: tracker.viewportHeight,
                    contentHeight: tracker.contentHeight)
                holdTarget(target)
                position.scrollTo(y: target)
                // 端に達していても消費する (§5.7)。素通しすると macOS が
                // 未処理キーとしてビープを鳴らし、矢印はフォーカス移動に化ける
                return .handled
            }
    }

    /// 目標位置を短時間だけ保持する。キーを離せば解放されるので、
    /// そのあとのホイール操作やスクロールバーの位置を持ち越しで壊さない
    private func holdTarget(_ target: CGFloat) {
        tracker.pendingTarget = target
        tracker.pendingRelease?.cancel()
        tracker.pendingRelease = Task { [tracker] in
            try? await Task.sleep(for: .milliseconds(200))
            guard !Task.isCancelled else { return }
            tracker.pendingTarget = nil
        }
    }

    private static func command(for press: KeyPress) -> KeyboardScroll.Command? {
        let toDocumentEdge = press.modifiers.contains(.command)
        switch press.key {
        case .upArrow: return toDocumentEdge ? .top : .lineUp
        case .downArrow: return toDocumentEdge ? .bottom : .lineDown
        case .pageUp: return .pageUp
        case .pageDown: return .pageDown
        case .home: return .top
        case .end: return .bottom
        case .space: return press.modifiers.contains(.shift) ? .pageUp : .pageDown
        default: return nil
        }
    }
}
