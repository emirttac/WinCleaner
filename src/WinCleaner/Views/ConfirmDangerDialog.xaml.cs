using System.Windows;

namespace WinCleaner.Views;

public partial class ConfirmDangerDialog : Window
{
    public ConfirmDangerDialog(string? title = null, string? message = null, string? acknowledgeLabel = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
            TitleText.Text = title;
        }
        if (!string.IsNullOrWhiteSpace(message))
            MessageText.Text = message;
        if (!string.IsNullOrWhiteSpace(acknowledgeLabel))
            AckCheck.Content = acknowledgeLabel;

        AckCheck.Checked += (_, _) => OkButton.IsEnabled = true;
        AckCheck.Unchecked += (_, _) => OkButton.IsEnabled = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
