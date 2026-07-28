using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TweetViewer.ViewModels;

/// <summary>
/// タイムラインに出す画像 1 枚。表示倍率は「見ている間だけ大きくして確認する」用途の
/// セッション状態で、DB には持たない — リスト再構築 (ユーザー切替・絞り込み・再起動) で
/// 等倍に戻る。コンテナはリサイクルされるので、状態は必ずここ (VM) に置くこと。
/// </summary>
public sealed partial class TweetImageViewModel : ObservableObject
{
    private readonly ImageBaseSize _baseSize;

    public TweetImageViewModel(string localPath, ImageBaseSize baseSize)
    {
        LocalPath = localPath;
        _baseSize = baseSize;
    }

    /// <summary>
    /// 解決済みのローカルパス (実ファイルが無いものは呼び出し側で除外済み)。
    /// Path という名前にすると System.IO.Path を隠してしまうため LocalPath。
    /// </summary>
    public string LocalPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxDisplayWidth))]
    [NotifyPropertyChangedFor(nameof(MaxDisplayHeight))]
    [NotifyPropertyChangedFor(nameof(DecodeWidth))]
    private ImageScale _scale = ImageScale.Normal;

    public double MaxDisplayWidth => ImageScales.Scaled(_baseSize.MaxWidth, Scale);
    public double MaxDisplayHeight => ImageScales.Scaled(_baseSize.MaxHeight, Scale);

    /// <summary>
    /// StretchDirection=DownOnly なので、拡大にはデコード画素そのものを増やす必要がある
    /// (Stretch を変えると 400px の絵を引き伸ばしたボケた絵になり、確認用途に使えない)。
    /// </summary>
    public int DecodeWidth => ImageScales.ScaledDecodeWidth(_baseSize.DecodeWidth, Scale);

    [RelayCommand]
    private void SetScale(ImageScale scale) => Scale = scale;
}
