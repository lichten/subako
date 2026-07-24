using System.Windows;
using System.Windows.Input;

namespace TweetViewer.Views;

public partial class AddTagDialog : Window
{
    public string? TagName { get; private set; }

    public AddTagDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TagNameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Commit();

    private void TagNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Commit();
    }

    private void Commit()
    {
        TagName = TagNameBox.Text.Trim();
        DialogResult = TagName.Length > 0;
    }
}
