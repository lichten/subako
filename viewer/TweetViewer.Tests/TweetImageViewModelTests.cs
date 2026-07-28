using TweetViewer.ViewModels;

namespace TweetViewer.Tests;

/// <summary>画像 1 枚ぶんの表示倍率 VM (DB / WPF ツリー非依存)。</summary>
public class TweetImageViewModelTests
{
    private static TweetImageViewModel CreateBody() =>
        new(@"C:\data\alice\images\100_1.jpg", ImageBaseSize.Body);

    [Fact]
    public void 既定は等倍()
    {
        var vm = CreateBody();

        Assert.Equal(ImageScale.Normal, vm.Scale);
        Assert.Equal(400, vm.MaxDisplayWidth);
        Assert.Equal(280, vm.MaxDisplayHeight);
        Assert.Equal(400, vm.DecodeWidth);
    }

    [Fact]
    public void SetScaleCommand_で倍率が変わる()
    {
        // RelayCommand<ImageScale> のパラメータ変換が通ることの確認も兼ねる
        // (double にすると CommandParameter の文字列キャストで実行時例外になる)
        var vm = CreateBody();

        vm.SetScaleCommand.Execute(ImageScale.Double);

        Assert.Equal(ImageScale.Double, vm.Scale);
        Assert.Equal(800, vm.MaxDisplayWidth);
        Assert.Equal(560, vm.MaxDisplayHeight);
        Assert.Equal(800, vm.DecodeWidth);
    }

    [Fact]
    public void 倍率変更で派生プロパティすべてに通知が飛ぶ()
    {
        // NotifyPropertyChangedFor を 1 つでも落とすと XAML 側で無言で壊れるため固定する
        var vm = CreateBody();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Scale = ImageScale.Quadruple;

        Assert.Contains(nameof(TweetImageViewModel.MaxDisplayWidth), changed);
        Assert.Contains(nameof(TweetImageViewModel.MaxDisplayHeight), changed);
        Assert.Contains(nameof(TweetImageViewModel.DecodeWidth), changed);
    }

    [Fact]
    public void 引用先の基準サイズは本文より小さい()
    {
        var quoted = new TweetImageViewModel(@"C:\data\alice\images\100_2.jpg", ImageBaseSize.Quoted);

        Assert.Equal(260, quoted.MaxDisplayWidth);
        Assert.Equal(180, quoted.MaxDisplayHeight);
        Assert.Equal(260, quoted.DecodeWidth);
    }

    [Fact]
    public void LocalPath_は倍率に影響されない()
    {
        var vm = CreateBody();
        var path = vm.LocalPath;

        vm.Scale = ImageScale.Half;

        Assert.Equal(path, vm.LocalPath);
    }
}
