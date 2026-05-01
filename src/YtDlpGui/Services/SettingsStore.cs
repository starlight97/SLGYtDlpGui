using System.IO;
using System.Text.Json;
using YtDlpGui.Models;

namespace YtDlpGui.Services;

public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Tolerate unknown / removed fields across version bumps.
        PropertyNameCaseInsensitive = true,
    };

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YtDlpGui");
        try { Directory.CreateDirectory(dir); } catch { /* will fail on Save; we log nothing yet */ }
        SettingsFilePath = Path.Combine(dir, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; private set; }
    public string SettingsFilePath { get; }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return new AppSettings();
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or partially-written file: start fresh; Save will overwrite.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            // Atomic-ish: write to a temp file then move.
            var tmp = SettingsFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SettingsFilePath)) File.Replace(tmp, SettingsFilePath, destinationBackupFileName: null);
            else File.Move(tmp, SettingsFilePath);
        }
        catch
        {
            // Settings are non-critical convenience state.
        }
    }
}
