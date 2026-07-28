using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TweetViewer.Behaviors;

/// <summary>
/// Image.Source にファイルパスを直接バインドすると原寸デコードで
/// メモリを食い潰すため、縮小デコード(DecodePixelWidth)+ Freeze を
/// バックグラウンドで行う添付プロパティ。コンテナリサイクルで
/// パスが差し替わった場合は古いデコード結果を捨てる。
/// </summary>
public static class ImagePathBehavior
{
    private const int DefaultDecodeWidth = 400;

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.RegisterAttached(
            "Path", typeof(string), typeof(ImagePathBehavior),
            new PropertyMetadata(null, OnPathChanged));

    public static string? GetPath(DependencyObject obj) => (string?)obj.GetValue(PathProperty);
    public static void SetPath(DependencyObject obj, string? value) => obj.SetValue(PathProperty, value);

    /// <summary>デコード幅 (既定 400)。0 以下で原寸デコード。アイコン等は小さい値を指定。</summary>
    public static readonly DependencyProperty DecodeWidthProperty =
        DependencyProperty.RegisterAttached(
            "DecodeWidth", typeof(int), typeof(ImagePathBehavior),
            new PropertyMetadata(DefaultDecodeWidth, OnDecodeWidthChanged));

    public static int GetDecodeWidth(DependencyObject obj) => (int)obj.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(DependencyObject obj, int value) => obj.SetValue(DecodeWidthProperty, value);

    /// <summary>読み込み世代。積んだ後に Path / DecodeWidth が動いたかの判定に使う。</summary>
    private static readonly DependencyProperty ReloadTokenProperty =
        DependencyProperty.RegisterAttached(
            "ReloadToken", typeof(int), typeof(ImagePathBehavior), new PropertyMetadata(0));

    /// <summary>Dispatcher に未実行の読み込みが積まれているか。</summary>
    private static readonly DependencyProperty ReloadPendingProperty =
        DependencyProperty.RegisterAttached(
            "ReloadPending", typeof(bool), typeof(ImagePathBehavior), new PropertyMetadata(false));

    private static void OnPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
            return;
        // 別ツイートへのリサイクル。前の絵を必ず消してから読み直す
        image.Source = null;
        ScheduleReload(image);
    }

    private static void OnDecodeWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 表示サイズ変更。Source は残したまま差し替える (消すと拡大のたびに灰色の箱が一瞬出る)
        if (d is Image image)
            ScheduleReload(image);
    }

    /// <summary>
    /// Path と DecodeWidth はコンテナのリサイクル時に同じ DataContext 更新で続けて変わる。
    /// Dispatcher に 1 回だけ積んでまとめ、2 重デコード (スクロール中のディスク/CPU 倍増) を防ぐ。
    /// </summary>
    private static void ScheduleReload(Image image)
    {
        // pending の確認より先に世代を進める。積んである処理が最新値を読むようにするため
        image.SetValue(ReloadTokenProperty, (int)image.GetValue(ReloadTokenProperty) + 1);
        if ((bool)image.GetValue(ReloadPendingProperty))
            return;
        image.SetValue(ReloadPendingProperty, true);
        // Loaded (6) は Input (5) より上なので連続スクロール中も飢餓にならず、
        // レイアウト後に走るので measure と競合しない
        _ = image.Dispatcher.InvokeAsync(() =>
        {
            image.SetValue(ReloadPendingProperty, false);
            _ = ReloadAsync(image, (int)image.GetValue(ReloadTokenProperty));
        }, DispatcherPriority.Loaded);
    }

    private static async Task ReloadAsync(Image image, int token)
    {
        var path = GetPath(image);
        if (string.IsNullOrEmpty(path))
        {
            image.Source = null;
            return;
        }
        // 依存関係プロパティは UI スレッド専有。Task.Run の中から読まないよう先に取り出す
        var decodeWidth = GetDecodeWidth(image);
        var bitmap = await Task.Run(() => Decode(path, decodeWidth));
        // デコード中にリサイクル / サイズ変更が入っていたら捨てる (後発の読み込みが正しい絵を入れる)
        if ((int)image.GetValue(ReloadTokenProperty) == token)
            image.Source = bitmap;
    }

    private static BitmapImage? Decode(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            if (decodeWidth > 0)
                bitmap.DecodePixelWidth = decodeWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;   // 壊れた画像・未対応形式はプレースホルダのまま
        }
    }
}
