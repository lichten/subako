using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>画像の表示倍率の計算 (XAML 非依存の純ロジック)。</summary>
public class ImageScaleTests
{
    [Theory]
    [InlineData(ImageScale.Half, 0.5)]
    [InlineData(ImageScale.Normal, 1.0)]
    [InlineData(ImageScale.Double, 2.0)]
    [InlineData(ImageScale.Quadruple, 4.0)]
    public void Factor(ImageScale scale, double expected)
    {
        Assert.Equal(expected, ImageScales.Factor(scale));
    }

    [Fact]
    public void 基準サイズは従来のXAML_literalと一致する()
    {
        // 回帰ロック: 等倍のときに見た目が 1px も変わらないことの保証。
        // 変更前の MainWindow.xaml では本文が MaxHeight=280/MaxWidth=400 (DecodeWidth は
        // 既定の 400)、引用先が MaxHeight=180/MaxWidth=260/DecodeWidth=260 だった
        Assert.Equal(new ImageBaseSize(400, 280, 400), ImageBaseSize.Body);
        Assert.Equal(new ImageBaseSize(260, 180, 260), ImageBaseSize.Quoted);
    }

    [Theory]
    [InlineData(ImageScale.Half, 200, 140, 200)]
    [InlineData(ImageScale.Normal, 400, 280, 400)]
    [InlineData(ImageScale.Double, 800, 560, 800)]
    [InlineData(ImageScale.Quadruple, 1600, 1120, 1600)]
    public void 本文画像の実寸(ImageScale scale, double width, double height, int decodeWidth)
    {
        var b = ImageBaseSize.Body;

        Assert.Equal(width, ImageScales.Scaled(b.MaxWidth, scale));
        Assert.Equal(height, ImageScales.Scaled(b.MaxHeight, scale));
        Assert.Equal(decodeWidth, ImageScales.ScaledDecodeWidth(b.DecodeWidth, scale));
    }

    [Theory]
    [InlineData(ImageScale.Half, 130, 90, 130)]
    [InlineData(ImageScale.Normal, 260, 180, 260)]
    [InlineData(ImageScale.Double, 520, 360, 520)]
    [InlineData(ImageScale.Quadruple, 1040, 720, 1040)]
    public void 引用先画像の実寸(ImageScale scale, double width, double height, int decodeWidth)
    {
        var q = ImageBaseSize.Quoted;

        Assert.Equal(width, ImageScales.Scaled(q.MaxWidth, scale));
        Assert.Equal(height, ImageScales.Scaled(q.MaxHeight, scale));
        Assert.Equal(decodeWidth, ImageScales.ScaledDecodeWidth(q.DecodeWidth, scale));
    }

    [Fact]
    public void デコード幅は上限でクランプされる()
    {
        // 1024 * 4 = 4096 だが、1 枚で数十 MB のビットマップを作らないよう頭打ちにする
        Assert.Equal(ImageScales.MaxDecodeWidth,
            ImageScales.ScaledDecodeWidth(1024, ImageScale.Quadruple));
    }

    [Fact]
    public void 既定のプリセットは上限に届かない()
    {
        Assert.True(ImageScales.ScaledDecodeWidth(ImageBaseSize.Body.DecodeWidth, ImageScale.Quadruple)
                    < ImageScales.MaxDecodeWidth);
    }
}
