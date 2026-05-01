using Wpf.Ui.Appearance;

namespace YtDlpGui.Infrastructure;

/// <summary>
/// Single point where the persisted <c>AppSettings.Theme</c> string is mapped
/// onto WPF-UI's <see cref="ApplicationThemeManager"/>. Called at startup and
/// whenever Settings is saved.
/// </summary>
public static class ThemeApplier
{
    public static void Apply(string? theme)
    {
        var requested = (theme ?? "System").Trim();
        var resolved = requested switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark"  => ApplicationTheme.Dark,
            _       => ResolveSystem(),
        };
        ApplicationThemeManager.Apply(resolved);
    }

    private static ApplicationTheme ResolveSystem()
    {
        try
        {
            // SystemThemeManager.UpdateSystemThemeCache() is needed before reading on some versions.
            return ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
        }
        catch
        {
            return ApplicationTheme.Light;
        }
    }
}
