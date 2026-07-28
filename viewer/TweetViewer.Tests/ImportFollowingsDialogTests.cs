using TweetViewer.Models;
using TweetViewer.ViewModels;
using TweetViewer.Views;

namespace TweetViewer.Tests;

/// <summary>
/// 「フォロー中を一括登録」ダイアログ。Grid.Row を付け忘れると全要素が行 0 に
/// 重なる (過去に同じ不具合を出しているため実際にレイアウトを走らせて確認する)。
/// </summary>
public class ImportFollowingsDialogTests
{
    [Theory]
    [InlineData("A,B", new[] { "A", "B" })]
    [InlineData("A，B、C", new[] { "A", "B", "C" })]            // 全角カンマ・読点
    [InlineData(" A , B ", new[] { "A", "B" })]                 // 前後の空白は落とす
    [InlineData("A\nB\r\nC", new[] { "A", "B", "C" })]          // 改行区切り
    [InlineData("A,,B,", new[] { "A", "B" })]                   // 空要素は落とす
    [InlineData("A,a,A", new[] { "A" })]                        // 大文字小文字無視で重複除去
    [InlineData("", new string[0])]
    [InlineData("  ,  ", new string[0])]
    public void ParseTagNames(string input, string[] expected)
    {
        Assert.Equal(expected, ImportFollowingsDialog.ParseTagNames(input));
    }

    [Fact]
    public void ElementsDoNotOverlapAndDefaultsAreSet()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var tags = new[]
                {
                    new TagItemViewModel(new TagRow { TagId = 1, Name = "ゲーム" }),
                    new TagItemViewModel(new TagRow { TagId = 2, Name = "絵師" }),
                };
                var dialog = new ImportFollowingsDialog(tags);
                Assert.Equal(0, dialog.MaxRequests);   // 開始前は未確定
                Assert.Empty(dialog.SelectedTagIds);
                Assert.Equal("", dialog.SourceUsername);

                var root = (System.Windows.Controls.Grid)dialog.Content;
                root.Measure(new System.Windows.Size(dialog.Width, dialog.Height));
                root.Arrange(new System.Windows.Rect(
                    new System.Windows.Point(0, 0), root.DesiredSize));
                root.UpdateLayout();

                var previousBottom = double.NegativeInfinity;
                var previousName = "(先頭)";
                foreach (var name in new[]
                         { "SourceBox", "TagListBox", "NewTagsBox", "MaxRequestsBox" })
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

                var box = (System.Windows.Controls.TextBox)dialog.FindName("MaxRequestsBox")!;
                Assert.Equal("50", box.Text);

                // タグ一覧は渡したタグで埋まり、初期状態は全て未チェック
                var list = (System.Windows.Controls.ListBox)dialog.FindName("TagListBox")!;
                var items = list.ItemsSource.Cast<TagCheckItem>().ToList();
                Assert.Equal(new[] { "ゲーム", "絵師" }, items.Select(i => i.Name));
                Assert.All(items, i => Assert.False(i.IsChecked));
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
