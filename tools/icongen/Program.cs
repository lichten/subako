using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Subako のアプリケーションアイコン (巣箱) を生成する。
//
//   dotnet run --project tools/icongen -- <出力先フォルダ>
//
// 出力: subako.ico (16/32/48 = BMP エントリ, 256 = PNG エントリ) と
//       subako-256.png (README 等で使う元画像)、確認用プレビュー。
// デザインの「元データ」はこのコード自体 — 再実行すれば同じアイコンが得られる
// (docs/release-plan.md §3-1)。

var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

int[] sizes = [16, 32, 48, 256];
var pngs = sizes.ToDictionary(s => s, RenderPng);

File.WriteAllBytes(Path.Combine(outDir, "subako-256.png"), pngs[256]);
WriteIco(Path.Combine(outDir, "subako.ico"), pngs);
// 小サイズの見え方の確認用 (最近傍拡大でドットの潰れを見る)
File.WriteAllBytes(Path.Combine(outDir, "preview-16.png"), UpscalePreview(pngs[16], 16, 8));
File.WriteAllBytes(Path.Combine(outDir, "preview-32.png"), UpscalePreview(pngs[32], 32, 4));
Console.WriteLine($"generated: {Path.GetFullPath(outDir)}");

static byte[] RenderPng(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
        DrawNestBox(dc, size);
    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

/// <summary>巣箱: 切妻屋根 + 木の本体 + 丸い入口 + 止まり木。</summary>
static void DrawNestBox(DrawingContext dc, int size)
{
    double s = size;
    var tiny = size <= 16;   // 16px では止まり木を省き、穴を大きめにして視認性を保つ

    var bodyBrush = new SolidColorBrush(Color.FromRgb(0xD2, 0x8E, 0x47));
    var roofBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x43, 0x1F));
    var holeBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x1D, 0x12));
    var edgePen = new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x54, 0x26)), Math.Max(1.0, 0.02 * s));

    // 本体 (角を少し丸めた木箱)
    var body = new Rect(0.18 * s, 0.34 * s, 0.64 * s, 0.60 * s);
    dc.DrawRoundedRectangle(bodyBrush, edgePen, body, 0.05 * s, 0.05 * s);

    // 屋根 (本体より庇を出した三角)
    var roof = new StreamGeometry();
    using (var ctx = roof.Open())
    {
        ctx.BeginFigure(new Point(0.05 * s, 0.40 * s), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(0.50 * s, 0.05 * s), isStroked: true, isSmoothJoin: true);
        ctx.LineTo(new Point(0.95 * s, 0.40 * s), isStroked: true, isSmoothJoin: true);
    }
    dc.DrawGeometry(roofBrush, null, roof);

    // 入口の丸穴
    var holeRadius = (tiny ? 0.17 : 0.15) * s;
    dc.DrawEllipse(holeBrush, null, new Point(0.50 * s, 0.60 * s), holeRadius, holeRadius);

    // 止まり木 (小サイズでは潰れるので省略)
    if (!tiny)
        dc.DrawEllipse(roofBrush, null, new Point(0.50 * s, 0.85 * s), 0.05 * s, 0.05 * s);
}

/// <summary>16/32/48 は BMP エントリ、256 は PNG エントリで .ico に固める。</summary>
static void WriteIco(string path, Dictionary<int, byte[]> pngs)
{
    var entries = new List<(int Size, byte[] Data)>
    {
        (16, BmpEntryFromPng(pngs[16], 16)),
        (32, BmpEntryFromPng(pngs[32], 32)),
        (48, BmpEntryFromPng(pngs[48], 48)),
        (256, pngs[256]),   // 256 は PNG 圧縮エントリが仕様上の標準
    };

    using var writer = new BinaryWriter(File.Create(path));
    writer.Write((ushort)0);                 // reserved
    writer.Write((ushort)1);                 // type = icon
    writer.Write((ushort)entries.Count);
    var offset = 6 + 16 * entries.Count;
    foreach (var (size, data) in entries)
    {
        writer.Write((byte)(size == 256 ? 0 : size));   // 0 = 256
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);               // palette
        writer.Write((byte)0);               // reserved
        writer.Write((ushort)1);             // planes
        writer.Write((ushort)32);            // bpp
        writer.Write(data.Length);
        writer.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in entries)
        writer.Write(data);
}

/// <summary>PNG を 32bpp BGRA の ICO 用 BMP (二重高さ + AND マスク) に変換する。</summary>
static byte[] BmpEntryFromPng(byte[] png, int size)
{
    var decoder = new PngBitmapDecoder(
        new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    // Pbgra32 (乗算済み) のままだと半透明の縁が黒ずむため、通常の BGRA に戻す
    var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
    var stride = size * 4;
    var pixels = new byte[stride * size];
    converted.CopyPixels(pixels, stride, 0);

    using var ms = new MemoryStream();
    using var writer = new BinaryWriter(ms);
    writer.Write(40);                        // BITMAPINFOHEADER
    writer.Write(size);
    writer.Write(size * 2);                  // XOR + AND を含む二重高さ
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(0);                         // BI_RGB
    writer.Write(stride * size);
    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
    for (var y = size - 1; y >= 0; y--)      // ボトムアップ
        writer.Write(pixels, y * stride, stride);
    var maskRowBytes = (size + 31) / 32 * 4; // 1bpp、行は 32bit 境界
    writer.Write(new byte[maskRowBytes * size]);   // 透過はアルファで表すので全ゼロ
    return ms.ToArray();
}

/// <summary>最近傍で拡大したプレビュー PNG (小サイズの潰れ確認用)。</summary>
static byte[] UpscalePreview(byte[] png, int size, int factor)
{
    var decoder = new PngBitmapDecoder(
        new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    var visual = new DrawingVisual();
    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
    using (var dc = visual.RenderOpen())
        dc.DrawImage(decoder.Frames[0], new Rect(0, 0, size * factor, size * factor));
    var bitmap = new RenderTargetBitmap(size * factor, size * factor, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}
