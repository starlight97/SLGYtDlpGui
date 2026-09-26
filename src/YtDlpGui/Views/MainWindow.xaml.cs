using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using YtDlpGui.ViewModels;

namespace YtDlpGui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Activated += OnActivated;
        Closing += OnClosing;
    }

    // Auto-scroll the per-item log to the bottom whenever new output arrives.
    private void ItemLog_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }

    /// <summary>SPEC §5.1: scan the clipboard for URLs each time the user comes back to the app.</summary>
    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.OnWindowActivated();
    }

    /// <summary>SPEC §12: warn before tearing down active downloads.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!vm.HasActiveDownloads) return;

        var n = vm.ActiveCount;
        var result = MessageBox.Show(
            this,
            $"There {(n == 1 ? "is" : "are")} {n} active download{(n == 1 ? string.Empty : "s")}. Cancel and exit?",
            "Active downloads",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        // Kick the cancel before the window goes away so the queue gets a chance to tear down cleanly.
        if (vm.CancelAllCommand.CanExecute(null)) vm.CancelAllCommand.Execute(null);
    }
}
