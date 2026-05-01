using System.Windows;
using YtDlpGui.ViewModels;

namespace YtDlpGui.Views;

public partial class FormatPickerWindow : Window
{
    public FormatPickerWindow(FormatPickerViewModel vm)
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
