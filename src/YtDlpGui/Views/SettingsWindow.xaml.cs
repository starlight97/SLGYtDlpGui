using System.Windows;
using YtDlpGui.ViewModels;

namespace YtDlpGui.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += OnRequestClose;

        void OnRequestClose()
        {
            vm.RequestClose -= OnRequestClose;
            DialogResult = vm.DialogResult;
            Close();
        }
    }
}
