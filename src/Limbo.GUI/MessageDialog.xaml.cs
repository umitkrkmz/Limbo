using System.Windows;

namespace Limbo.GUI;

public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message, string icon = "✅")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = icon;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Show a themed success message
    /// </summary>
    public static void ShowSuccess(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, "✅") { Owner = owner };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Show a themed info message
    /// </summary>
    public static void ShowInfo(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, "ℹ️") { Owner = owner };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Show a themed warning message
    /// </summary>
    public static void ShowWarning(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, "⚠️") { Owner = owner };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Show a themed error message
    /// </summary>
    public static void ShowError(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, "❌") { Owner = owner };
        dialog.ShowDialog();
    }
}
