using System.Windows;
using System.Windows.Input;

namespace TweetViewer.Views;

public partial class AddUserDialog : Window
{
    public string? Username { get; private set; }

    public AddUserDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Commit();

    private void UsernameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Commit();
    }

    private void Commit()
    {
        Username = UsernameBox.Text.Trim().TrimStart('@');
        DialogResult = Username.Length > 0;
    }
}
