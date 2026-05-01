using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using YtDlpGui.Services;
using YtDlpGui.ViewModels;
using YtDlpGui.Views;

namespace YtDlpGui;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddSingleton<IBinaryResolver, BinaryResolver>();
        services.AddSingleton<IYtDlpRunner, YtDlpRunner>();
        services.AddSingleton<IDownloadQueue, DownloadQueue>();

        services.AddSingleton<OptionsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var window = Services.GetRequiredService<MainWindow>();
        window.DataContext = Services.GetRequiredService<MainViewModel>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable d) d.Dispose();
        base.OnExit(e);
    }
}
