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
        // The IAppUpdateService singleton outlives this transient VM — unsubscribe its StatusChanged
        // handler here or a new SettingsViewModel (and its handler) leaks on every dialog open.
        Closed += (_, _) => vm.OnWindowClosed();

        void OnRequestClose()
        {
            vm.RequestClose -= OnRequestClose;
            DialogResult = vm.DialogResult;
            Close();
        }
    }
}
