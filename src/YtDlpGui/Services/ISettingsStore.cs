using YtDlpGui.Models;

namespace YtDlpGui.Services;

public interface ISettingsStore
{
    /// <summary>The live settings object. Mutate fields then call <see cref="Save"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Absolute path to the JSON file (for the Settings dialog "Open settings folder" link).</summary>
    string SettingsFilePath { get; }

    /// <summary>Persist the current settings to disk. Failures are swallowed (settings are convenience).</summary>
    void Save();
}
