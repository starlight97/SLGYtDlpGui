using System.Windows;

namespace YtDlpGui.Views;

/// <summary>
/// Single-line input prompt. Used by the profile "Save as…" flow. Avoids a
/// dependency on Microsoft.VisualBasic.Interaction.InputBox.
/// </summary>
public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = prompt;
        ValueBox.Text = initial;
        ValueBox.SelectAll();
        Loaded += (_, _) => ValueBox.Focus();
    }

    public string Value => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = !string.IsNullOrWhiteSpace(ValueBox.Text);
        if (DialogResult == true) Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string? Prompt(Window? owner, string title, string prompt, string initial = "")
    {
        var dlg = new InputDialog(title, prompt, initial);
        if (owner is not null) dlg.Owner = owner;
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
