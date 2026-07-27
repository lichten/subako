namespace TweetViewer.Tests;

/// <summary>
/// 「表示中をすべて更新」ダイアログの XAML 解析とレイアウト。
/// Grid.Row を付け忘れると全要素が行 0 に重なる (過去に同じ不具合を出しているため
/// 実際にレイアウトを走らせて重なりが無いことを確認する)。
/// </summary>
public class UpdateAllDialogTests
{
    [Fact]
    public void ElementsDoNotOverlapAndDefaultsAreSet()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new Views.UpdateAllDialog(userCount: 8, searchCount: 3);
                Assert.Equal(0, dialog.MaxRequests);   // 開始前は未確定

                var root = (System.Windows.Controls.Grid)dialog.Content;
                root.Measure(new System.Windows.Size(dialog.Width, double.PositiveInfinity));
                root.Arrange(new System.Windows.Rect(
                    new System.Windows.Point(0, 0), root.DesiredSize));
                root.UpdateLayout();

                var previousBottom = double.NegativeInfinity;
                var previousName = "(先頭)";
                foreach (var name in new[] { "TargetText", "MaxRequestsBox" })
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

                // ボタン行が最下段にあること
                var buttons = root.Children.OfType<System.Windows.Controls.StackPanel>().Last();
                var buttonsTop = buttons.TransformToAncestor(root)
                    .Transform(new System.Windows.Point(0, 0)).Y;
                Assert.True(buttonsTop >= previousBottom, "ボタン行が直前の要素と重なっています");

                // 対象件数の表示と既定値
                var target = (System.Windows.Controls.TextBlock)dialog.FindName("TargetText")!;
                Assert.Contains("ユーザー 8 件", target.Text);
                Assert.Contains("検索 3 件", target.Text);
                var box = (System.Windows.Controls.TextBox)dialog.FindName("MaxRequestsBox")!;
                Assert.Equal("100", box.Text);
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
            throw new Xunit.Sdk.XunitException($"ダイアログのテストが失敗: {failure}");
    }
}
