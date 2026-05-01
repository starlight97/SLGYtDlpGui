using System.Windows;
using System.Windows.Controls;

namespace YtDlpGui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Auto-scroll the per-item log to the bottom whenever new output arrives.
    private void ItemLog_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }
}
