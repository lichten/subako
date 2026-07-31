import AppKit
import CoreGraphics
import Foundation

// Subako のアプリケーションアイコン (巣箱) を生成する。
//
//   swift run subako-icongen <出力先フォルダ>
//
// 出力: Subako.iconset/ (iconutil が要求する 10 枚の PNG)。
// .icns への変換は Scripts/make-icon.sh が iconutil で行う。
//
// これは tools/icongen/Program.cs (C# + WPF、Windows 専用) の移植。
// **図形の比率は Windows 版と完全に同一にすること** — 片方だけ直すと
// 両プラットフォームでアイコンの見た目がずれる。
// デザインの「元データ」はこのコード自体 (docs/release-plan.md §3-1)。

let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

func srgb(_ r: Int, _ g: Int, _ b: Int) -> CGColor {
    CGColor(
        colorSpace: sRGB,
        components: [CGFloat(r) / 255, CGFloat(g) / 255, CGFloat(b) / 255, 1])!
}

/// 巣箱: 切妻屋根 + 木の本体 + 丸い入口 + 止まり木。
/// 座標は一辺 s に対する比率。左上原点・y 下向き (WPF に合わせる)。
func drawNestBox(in context: CGContext, size: Int) {
    let s = CGFloat(size)
    // 16px では止まり木を省き、穴を大きめにして視認性を保つ
    let tiny = size <= 16

    // 色は sRGB として指定する。CGColor(red:green:blue:alpha:) は generic RGB になり、
    // sRGB のコンテキストへ描くと変換で明るくずれる (実測で Windows 版と色が違った)
    let body = srgb(0xD2, 0x8E, 0x47)
    let roof = srgb(0x6B, 0x43, 0x1F)
    let hole = srgb(0x2B, 0x1D, 0x12)
    let edge = srgb(0x8A, 0x54, 0x26)

    // CoreGraphics は y 上向きなので、y を反転して WPF と同じ座標で書けるようにする
    context.translateBy(x: 0, y: s)
    context.scaleBy(x: 1, y: -1)

    // 本体 (角を少し丸めた木箱)
    let bodyRect = CGRect(x: 0.18 * s, y: 0.34 * s, width: 0.64 * s, height: 0.60 * s)
    let bodyPath = CGPath(
        roundedRect: bodyRect, cornerWidth: 0.05 * s, cornerHeight: 0.05 * s, transform: nil)
    context.setFillColor(body)
    context.setStrokeColor(edge)
    context.setLineWidth(max(1.0, 0.02 * s))
    context.addPath(bodyPath)
    context.drawPath(using: .fillStroke)

    // 屋根 (本体より庇を出した三角)
    context.setFillColor(roof)
    context.move(to: CGPoint(x: 0.05 * s, y: 0.40 * s))
    context.addLine(to: CGPoint(x: 0.50 * s, y: 0.05 * s))
    context.addLine(to: CGPoint(x: 0.95 * s, y: 0.40 * s))
    context.closePath()
    context.fillPath()

    // 入口の丸穴
    let holeRadius = (tiny ? 0.17 : 0.15) * s
    context.setFillColor(hole)
    context.fillEllipse(in: CGRect(
        x: 0.50 * s - holeRadius, y: 0.60 * s - holeRadius,
        width: holeRadius * 2, height: holeRadius * 2))

    // 止まり木 (小サイズでは潰れるので省略)
    if !tiny {
        let perch = 0.05 * s
        context.setFillColor(roof)
        context.fillEllipse(in: CGRect(
            x: 0.50 * s - perch, y: 0.85 * s - perch, width: perch * 2, height: perch * 2))
    }
}

func renderPNG(size: Int) throws -> Data {
    guard let context = CGContext(
        data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
        space: sRGB, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
    else { throw IconGenError("CGContext を作れません (size=\(size))") }
    context.interpolationQuality = .high
    context.setAllowsAntialiasing(true)
    drawNestBox(in: context, size: size)
    guard let image = context.makeImage() else {
        throw IconGenError("画像化に失敗 (size=\(size))")
    }
    let rep = NSBitmapImageRep(cgImage: image)
    rep.size = NSSize(width: size, height: size)
    guard let data = rep.representation(using: .png, properties: [:]) else {
        throw IconGenError("PNG 化に失敗 (size=\(size))")
    }
    return data
}

struct IconGenError: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}

/// iconutil が要求する組 (ファイル名, 実ピクセル数)
let entries: [(name: String, pixels: Int)] = [
    ("icon_16x16.png", 16),
    ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32),
    ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128),
    ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256),
    ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512),
    ("icon_512x512@2x.png", 1024),
]

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let iconset = (outDir as NSString).appendingPathComponent("Subako.iconset")
do {
    try FileManager.default.createDirectory(
        atPath: iconset, withIntermediateDirectories: true)
    // 同じサイズは 1 回だけ描いて使い回す
    var cache: [Int: Data] = [:]
    for entry in entries {
        let png = try cache[entry.pixels] ?? renderPNG(size: entry.pixels)
        cache[entry.pixels] = png
        try png.write(to: URL(fileURLWithPath:
            (iconset as NSString).appendingPathComponent(entry.name)))
    }
    print("generated: \(iconset)")
} catch {
    FileHandle.standardError.write(Data("アイコン生成に失敗: \(error)\n".utf8))
    exit(1)
}
