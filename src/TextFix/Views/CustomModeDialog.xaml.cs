using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace TextFix.Views;

public partial class CustomModeDialog : Window
{
    public string ModeName => NameBox.Text.Trim();
    public string ModePrompt => PromptBox.Text.Trim();

    public CustomModeDialog(string name = "", string prompt = "")
    {
        InitializeComponent();
        NameBox.Text = name;
        PromptBox.Text = prompt;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            WpfMessageBox.Show("Mode name is required.", "TextFix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
