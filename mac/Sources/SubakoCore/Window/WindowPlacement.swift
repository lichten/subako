import CoreGraphics
import Foundation

/// ウィンドウ配置の永続化にかかわる計算 (viewer-features.md §2.2)。
///
/// SwiftUI 任せにできない理由: `WindowGroup` はサイズこそ復元するが、位置は
/// 起動元アプリのある画面 (`NSScreen.main`) へ移し替えてしまう。しかも移動後の
/// 位置が保存されるため、マルチディスプレイでは一度ずれると戻らない (実測)。
/// そのため保存も復元も自前で行う。詳細は docs/mac-port-notes.md §4.6。
public enum WindowPlacement {
    /// 画面内と見なすのに必要な重なり面積の割合。角がわずかに掛かっただけでは救わない。
    static let minVisibleFraction: CGFloat = 0.2

    /// 保存された矩形を、実際に表示できる位置へ直して返す。
    ///
    /// - いずれかの画面に完全に収まっていればそのまま
    /// - 一番重なっている画面からはみ出していれば、その画面の中へ寄せる
    ///   (画面より大きければ縮める)
    /// - どの画面ともほとんど重なっていなければ `fallback` の中央へ
    ///
    /// macOS が自動で行う `constrainFrameRect(_:to:)` は「上端を画面に乗せる」
    /// 「高さを縮める」だけで、**横方向 (x 座標・幅) は補正されない**。
    /// 画面端をまたいだまま終了すると、その位置が保存されて毎回はみ出して開くので、
    /// ここで直す。画面をまたいで置いていたウィンドウは、重なりの大きい方へ寄る。
    public static func onScreen(
        _ frame: CGRect, screens: [CGRect], fallback: CGRect
    ) -> CGRect {
        guard frame.width > 0, frame.height > 0 else { return centered(frame, in: fallback) }
        let area = frame.width * frame.height
        var best: (screen: CGRect, overlap: CGFloat)?
        for screen in screens {
            let intersection = frame.intersection(screen)
            guard !intersection.isNull else { continue }
            let overlap = intersection.width * intersection.height
            if overlap > (best?.overlap ?? 0) {
                best = (screen, overlap)
            }
        }
        guard let best, best.overlap >= area * minVisibleFraction else {
            return centered(frame, in: fallback)
        }
        return fitted(frame, in: best.screen)
    }

    /// `frame` を `area` に収まるまで縮めてから、はみ出さない位置へ寄せる。
    static func fitted(_ frame: CGRect, in area: CGRect) -> CGRect {
        let width = min(max(frame.width, 1), area.width)
        let height = min(max(frame.height, 1), area.height)
        let x = min(max(frame.minX, area.minX), area.maxX - width)
        let y = min(max(frame.minY, area.minY), area.maxY - height)
        return CGRect(x: x, y: y, width: width, height: height)
    }

    /// `frame` のサイズを `area` に収まるまで縮めてから中央へ置く。
    static func centered(_ frame: CGRect, in area: CGRect) -> CGRect {
        let width = min(max(frame.width, 1), area.width)
        let height = min(max(frame.height, 1), area.height)
        return CGRect(
            x: area.minX + (area.width - width) / 2,
            y: area.minY + (area.height - height) / 2,
            width: width, height: height)
    }

    /// サイドバー幅の許容範囲 (§4: 既定 260、180〜600 で可変)。
    /// MainWindow の分割線のドラッグ範囲と一致させること。
    public static func clampSidebarWidth(_ width: Double) -> Double {
        min(max(width, 180), 600)
    }
}
