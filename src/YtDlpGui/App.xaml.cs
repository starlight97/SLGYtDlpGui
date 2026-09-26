using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YtDlpGui.Infrastructure;
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

        BootstrapLogging();
        Log.Information("YtDlpGui starting (v{Version})",
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?");

        var services = new ServiceCollection();

        // App-lifetime singletons.
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IBinaryResolver, BinaryResolver>();
        services.AddSingleton<IYtDlpRunner, YtDlpRunner>();
        services.AddSingleton<IFormatInspector, FormatInspector>();
        services.AddSingleton<IConfImporter, ConfImporter>();
        services.AddSingleton<IDownloadQueue, DownloadQueue>();
        services.AddSingleton<YtDlpUpdater>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();

        services.AddSingleton<OptionsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        // One per dialog opening.
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<FormatPickerViewModel>();

        Services = services.BuildServiceProvider();

        // Apply persisted theme before showing any window so it takes effect on first paint.
        var settings = Services.GetRequiredService<ISettingsStore>();
        try { ThemeApplier.Apply(settings.Current.Theme); }
        catch (Exception ex) { Log.Warning(ex, "Theme apply failed"); }

        var window = Services.GetRequiredService<MainWindow>();
        window.DataContext = Services.GetRequiredService<MainViewModel>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Persist Options panel state + parallelism so the next launch restores them.
        if (Services is not null)
        {
            try
            {
                var settings = Services.GetService<ISettingsStore>();
                var options = Services.GetService<OptionsViewModel>();
                var queue = Services.GetService<IDownloadQueue>();
                if (settings is not null)
                {
                    if (options is not null) settings.Current.LastOptions = options.Snapshot();
                    if (queue is not null) settings.Current.Parallelism = queue.Parallelism;
                    settings.Save();
                }
            }
            catch (Exception ex) { Log.Warning(ex, "OnExit settings save failed"); }
        }

        Log.Information("YtDlpGui exiting");
        Log.CloseAndFlush();

        if (Services is IDisposable d) d.Dispose();
        base.OnExit(e);
    }

    private static void BootstrapLogging()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YtDlpGui", "logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDir, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch
        {
            // If file logging can't be set up, fall back to a no-op logger so
            // Log.X calls don't blow up downstream.
            Log.Logger = new LoggerConfiguration().CreateLogger();
        }
    }
}
