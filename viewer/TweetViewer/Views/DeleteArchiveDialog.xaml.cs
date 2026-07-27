using System.Windows;
using TweetViewer.Services;

namespace TweetViewer.Views;

public partial class DeleteArchiveDialog : Window
{
    /// <summary>true ならディスク上のデータも削除する (false は _trash へ退避)。</summary>
    public bool DeleteFiles { get; private set; }

    /// <param name="headline">「@alice を削除しますか?」など。</param>
    /// <param name="tweetCount">保存済みツイート数 (削除される規模を伝える)。</param>
    public DeleteArchiveDialog(string headline, long tweetCount)
    {
        InitializeComponent();
        HeadlineText.Text = headline;
        CountText.Text = $"保存済みのツイート {tweetCount:N0}件のインデックスと既読以外の付随情報が削除されます。";
        UpdateNote();
        DeleteFilesBox.Checked += (_, _) => UpdateNote();
        DeleteFilesBox.Unchecked += (_, _) => UpdateNote();
    }

    private void UpdateNote() =>
        NoteText.Text = DeleteFilesBox.IsChecked == true
            ? "tweets.jsonl と画像を完全に削除します。復元できません。"
              + "同じ内容を揃えるには API リクエストを消費して取り直す必要があります。"
            : $"tweets.jsonl と画像は data/{ArchiveTrash.TrashDirName}/ へ移動して残します。"
              + "フォルダを元の場所に戻せば次回起動時に再登録され、既読状態も復元されます。";

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteFiles = DeleteFilesBox.IsChecked == true;
        DialogResult = true;
    }
}
