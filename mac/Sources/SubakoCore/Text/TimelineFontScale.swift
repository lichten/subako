import CoreGraphics

/// タイムライン文字サイズの倍率 (Mac 版独自 — mac/README.md「Windows 版仕様との既知差分」)。
/// 連続値ではなく段階を持たせるのは、⌘+/⌘- の 1 打ごとに変化が体感できる粒度に揃えるため。
public enum TimelineFontScale {
    public static let normal: CGFloat = 1.0

    /// 選択できる倍率。ブラウザのズーム段階に近い刻み
    public static let steps: [CGFloat] = [0.85, 1.0, 1.15, 1.3, 1.5, 1.75, 2.0]

    /// 未知の値 (手書きされた settings.json、将来の刻み変更) を最も近い段階に丸める。
    /// AppSettings は Optional から読むだけでバリデーション機構を持たないため、
    /// 壊れた値がそのまま描画に流れないようここで受け止める。
    public static func normalize(_ scale: CGFloat) -> CGFloat {
        guard scale.isFinite else { return normal }
        return steps.min { abs($0 - scale) < abs($1 - scale) } ?? normal
    }

    /// 1 段大きく。最大段ならそのまま
    public static func larger(than scale: CGFloat) -> CGFloat {
        let current = normalize(scale)
        guard let index = steps.firstIndex(of: current), index + 1 < steps.count else {
            return current
        }
        return steps[index + 1]
    }

    /// 1 段小さく。最小段ならそのまま
    public static func smaller(than scale: CGFloat) -> CGFloat {
        let current = normalize(scale)
        guard let index = steps.firstIndex(of: current), index > 0 else { return current }
        return steps[index - 1]
    }

    /// メニュー項目の有効/無効判定用
    public static func isLargest(_ scale: CGFloat) -> Bool {
        normalize(scale) == steps.last
    }

    public static func isSmallest(_ scale: CGFloat) -> Bool {
        normalize(scale) == steps.first
    }
}
