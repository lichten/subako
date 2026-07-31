import SwiftUI
import SubakoCore

/// スクロールビューの計測値。無限スクロール (§5.6) とキーボードスクロール (§5.7) で共用。
struct ScrollMetrics: Equatable {
    var offsetY: CGFloat = 0
    var contentHeight: CGFloat = 0
    var viewportHeight: CGFloat = 0
}

extension View {
    /// キーボードスクロール (§5.7)。
    /// `refocusOn` が変わるたびにフォーカスを取り戻す (サイドバーをクリックすると
    /// フォーカスを奪われ、以後キーが効かなくなるため)。
    func keyboardScroll<Trigger: Equatable>(
        metrics: ScrollMetrics,
        position: Binding<ScrollPosition>,
        refocusOn refocusTrigger: Trigger
    ) -> some View {
        modifier(KeyboardScrollModifier(
            metrics: metrics, position: position, refocusTrigger: refocusTrigger))
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
    let metrics: ScrollMetrics
    @Binding var position: ScrollPosition
    let refocusTrigger: Trigger

    @FocusState private var focused: Bool
    /// 直前に指示した目標位置。キーリピート中は計測値 (metrics) の更新が
    /// キーの速さに追いつかず、同じ位置から同じ目標を計算し直してしまうため、
    /// 反映待ちの間はこちらを基準にする
    @State private var pendingTarget: CGFloat?
    @State private var pendingRelease: Task<Void, Never>?

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
                pendingRelease?.cancel()
                pendingTarget = nil
            }
            // 既定では .down しか拾わないので、押しっぱなしのために .repeat も要る
            .onKeyPress(keys: Self.handledKeys, phases: [.down, .repeat]) { press in
                guard let command = Self.command(for: press) else { return .ignored }
                let target = KeyboardScroll.nextOffset(
                    command, offsetY: pendingTarget ?? metrics.offsetY,
                    viewportHeight: metrics.viewportHeight,
                    contentHeight: metrics.contentHeight)
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
        pendingTarget = target
        pendingRelease?.cancel()
        pendingRelease = Task {
            try? await Task.sleep(for: .milliseconds(200))
            guard !Task.isCancelled else { return }
            pendingTarget = nil
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
