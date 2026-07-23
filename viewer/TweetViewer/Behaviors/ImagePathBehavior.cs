using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

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
            new PropertyMetadata(DefaultDecodeWidth));

    public static int GetDecodeWidth(DependencyObject obj) => (int)obj.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(DependencyObject obj, int value) => obj.SetValue(DecodeWidthProperty, value);

    private static async void OnPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
            return;

        var path = (string?)e.NewValue;
        image.Source = null;
        if (string.IsNullOrEmpty(path))
            return;

        var decodeWidth = GetDecodeWidth(image);
        var bitmap = await Task.Run(() => Decode(path, decodeWidth));
        // デコード中にリサイクルで別パスに変わっていたら捨てる
        if (GetPath(image) == path)
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
