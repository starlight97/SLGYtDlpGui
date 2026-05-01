using System.Windows;
using YtDlpGui.Services;

namespace YtDlpGui.Views;

public partial class ConfImportDialog : Window
{
    public ConfImportDialog(ConfImportResult result, string filePath)
    {
        InitializeComponent();
        DataContext = new ConfImportSummary(filePath, result);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed record ConfImportSummary(
        string FilePath,
        IReadOnlyList<string> Applied,
        IReadOnlyList<string> Unrecognized,
        int AppliedCount,
        int UnrecognizedCount)
    {
        public ConfImportSummary(string filePath, ConfImportResult r)
            : this(filePath, r.AppliedFlags, r.UnrecognizedFlags, r.AppliedFlags.Count, r.UnrecognizedFlags.Count) { }
    }
}
