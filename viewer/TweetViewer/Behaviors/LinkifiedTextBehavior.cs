using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TweetViewer.Services;
using EmojiData = Emoji.Wpf.EmojiData;
using EmojiInline = Emoji.Wpf.EmojiInline;

namespace TweetViewer.Behaviors;

/// <summary>
/// 標準 TextBlock 用の添付プロパティ。テキストを
/// URL → Hyperlink / 絵文字 → EmojiInline / 通常文字 → Run に分解して表示する。
/// (Emoji.Wpf の TextBlock と同じ手法で絵文字をカラー描画しつつリンクを混在させる)
/// </summary>
public static class LinkifiedTextBehavior
{
    private static readonly Brush LinkBrush =
        new SolidColorBrush(Color.FromRgb(0x1D, 0x9B, 0xF0)).GetAsFrozen() as Brush
        ?? Brushes.DodgerBlue;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(LinkifiedTextBehavior),
            new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject obj) => (string?)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string? value) => obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
            return;

        textBlock.Inlines.Clear();
        if (e.NewValue is not string text || text.Length == 0)
            return;

        foreach (var segment in Linkifier.Split(text))
        {
            if (segment.IsUrl)
            {
                var link = new Hyperlink(new Run(segment.Text))
                {
                    Foreground = LinkBrush,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                var url = segment.Text;
                link.Click += (_, _) => Browser.OpenUrl(url);
                textBlock.Inlines.Add(link);
            }
            else
            {
                AddEmojiAwareInlines(textBlock, segment.Text);
            }
        }
    }

    /// <summary>Emoji.Wpf.TextBlock と同じ分割: EmojiData.MatchOne で絵文字列を EmojiInline に。</summary>
    private static void AddEmojiAwareInlines(TextBlock textBlock, string text)
    {
        var pos = 0;
        foreach (Match m in EmojiData.MatchOne.Matches(text))
        {
            if (m.Index != pos)
                textBlock.Inlines.Add(new Run(text[pos..m.Index]));
            textBlock.Inlines.Add(new EmojiInline
            {
                FontSize = textBlock.FontSize,
                Foreground = Brushes.Black,
                Text = m.Value,
            });
            pos = m.Index + m.Length;
        }
        if (pos != text.Length)
            textBlock.Inlines.Add(new Run(text[pos..]));
    }
}
