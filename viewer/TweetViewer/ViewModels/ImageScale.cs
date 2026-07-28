namespace TweetViewer.ViewModels;

/// <summary>
/// 画像の表示倍率 (右クリックメニューの選択肢)。セッション限りで永続化しない。
/// double ではなく enum なのは、RelayCommand&lt;double&gt; だと CommandParameter から
/// 渡る文字列を (double?) にキャストして実行時例外になるため。
/// </summary>
public enum ImageScale
{
    Half,
    Normal,
    Double,
    Quadruple,
}

/// <summary>倍率 1.0 のときの寸法。本文と引用先で違う (引用先は小さめという既存の設計)。</summary>
public readonly record struct ImageBaseSize(double MaxWidth, double MaxHeight, int DecodeWidth)
{
    /// <summary>本文・RT元の画像。MainWindow.xaml の従来の literal と同値。</summary>
    public static readonly ImageBaseSize Body = new(400, 280, 400);

    /// <summary>引用ブロック内の画像。MainWindow.xaml の従来の literal と同値。</summary>
    public static readonly ImageBaseSize Quoted = new(260, 180, 260);
}

/// <summary>倍率の適用 (XAML 非依存の純関数。テストはここに当てる)。</summary>
public static class ImageScales
{
    /// <summary>
    /// デコード幅の上限。既定の倍率では届かないが、倍率を増やしたときに
    /// 1 枚で数十 MB のビットマップを作らないための歯止め。
    /// </summary>
    public const int MaxDecodeWidth = 2048;

    public static double Factor(ImageScale scale) => scale switch
    {
        ImageScale.Half => 0.5,
        ImageScale.Double => 2.0,
        ImageScale.Quadruple => 4.0,
        _ => 1.0,
    };

    public static double Scaled(double baseValue, ImageScale scale) => baseValue * Factor(scale);

    public static int ScaledDecodeWidth(int baseDecodeWidth, ImageScale scale) =>
        Math.Min((int)Math.Round(baseDecodeWidth * Factor(scale)), MaxDecodeWidth);
}
