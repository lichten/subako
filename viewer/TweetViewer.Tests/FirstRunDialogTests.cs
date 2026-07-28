using TweetViewer.Views;

namespace TweetViewer.Tests;

/// <summary>
/// 初回セットアップと設定ダイアログのレイアウト。Grid.Row を付け忘れると全要素が
/// 行 0 に重なる (過去に同じ不具合を出しているため実際にレイアウトを走らせて確認する)。
/// </summary>
public class FirstRunDialogTests
{
    [Fact]
    public void 初回ダイアログの要素が重ならず既定値が入る()
    {
        RunSta(() =>
        {
            var settings = new AppSettings();
            var dialog = new FirstRunDialog(settings);

            AssertNoOverlap(dialog, ["WelcomeText", "DataDirBox"]);
            Assert.Contains(TweetViewer.AppInfo.Name, dialog.Title);
            Assert.EndsWith(TweetViewer.AppInfo.Name,
                ((System.Windows.Controls.TextBox)dialog.FindName("DataDirBox")!).Text);
        });
    }

    [Fact]
    public void 設定ダイアログの要素が重ならず現在値が入る()
    {
        RunSta(() =>
        {
            var settings = new AppSettings
            {
                DataDir = @"C:\data",
                RepoDir = @"C:\repo",
                PythonPath = "python3",
            };
            var dialog = new SettingsDialog(settings);

            AssertNoOverlap(dialog, ["DataDirBox", "RepoDirBox", "PythonPathBox"]);
            Assert.Equal(@"C:\data", ((System.Windows.Controls.TextBox)dialog.FindName("DataDirBox")!).Text);
            Assert.Equal(@"C:\repo", ((System.Windows.Controls.TextBox)dialog.FindName("RepoDirBox")!).Text);
            Assert.Equal("python3", ((System.Windows.Controls.TextBox)dialog.FindName("PythonPathBox")!).Text);
        });
    }

    private static void AssertNoOverlap(System.Windows.Window dialog, string[] namesInOrder)
    {
        var root = (System.Windows.Controls.Grid)dialog.Content;
        var height = double.IsNaN(dialog.Height) ? double.PositiveInfinity : dialog.Height;
        root.Measure(new System.Windows.Size(dialog.Width, height));
        root.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0), root.DesiredSize));
        root.UpdateLayout();

        var previousBottom = double.NegativeInfinity;
        var previousName = "(先頭)";
        foreach (var name in namesInOrder)
        {
            var element = (System.Windows.FrameworkElement)dialog.FindName(name)!;
            var top = element.TransformToAncestor(root)
                .Transform(new System.Windows.Point(0, 0)).Y;
            Assert.True(element.RenderSize.Height > 0, $"{name} の高さが 0 です");
            Assert.True(top >= previousBottom,
                $"{name} (top={top}) が {previousName} (bottom={previousBottom}) と重なっています");
            previousBottom = top + element.RenderSize.Height;
            previousName = name;
        }

        var buttons = root.Children.OfType<System.Windows.Controls.StackPanel>().Last();
        var buttonsTop = buttons.TransformToAncestor(root)
            .Transform(new System.Windows.Point(0, 0)).Y;
        Assert.True(buttonsTop >= previousBottom, "ボタン行が直前の要素と重なっています");
    }

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"STA スレッドのテストが失敗: {failure}");
    }
}
