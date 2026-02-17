using System.Windows;

namespace Limbo.GUI;

public partial class InputDialog : Window
{
    public string InputText { get; private set; } = "";

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();

        TitleText.Text = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        InputText = InputBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
