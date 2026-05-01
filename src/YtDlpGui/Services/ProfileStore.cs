using System.IO;
using System.Text.Json;
using YtDlpGui.Models;

namespace YtDlpGui.Services;

public sealed class ProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly List<Profile> _user = new();
    private readonly IReadOnlyList<Profile> _builtIns;

    public ProfileStore()
    {
        _builtIns = BuiltInProfiles().ToList();
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YtDlpGui");
        try { Directory.CreateDirectory(dir); } catch { /* will fail on Save */ }
        _path = Path.Combine(dir, "profiles.json");
        Load();
    }

    public IReadOnlyList<Profile> All => _builtIns.Concat(_user).ToList();

    public event Action? Changed;

    public bool ExistsUserProfile(string name) =>
        _user.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Upsert(Profile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile.Name is required");
        // Defensive: never write a built-in into the user list.
        profile.IsBuiltIn = false;
        var idx = _user.FindIndex(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _user[idx] = profile;
        else _user.Add(profile);
        Save();
        Changed?.Invoke();
    }

    public void Delete(string name)
    {
        var idx = _user.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        _user.RemoveAt(idx);
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<Profile>>(json, JsonOptions);
            if (list is null) return;
            foreach (var p in list)
            {
                p.IsBuiltIn = false;
                if (!string.IsNullOrWhiteSpace(p.Name)) _user.Add(p);
            }
        }
        catch { /* corrupt or partial — start clean */ }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_user, JsonOptions);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_path)) File.Replace(tmp, _path, destinationBackupFileName: null);
            else File.Move(tmp, _path);
        }
        catch { /* convenience state; don't surface */ }
    }

    /// <summary>SPEC §5.2: the four shipping presets.</summary>
    private static IEnumerable<Profile> BuiltInProfiles()
    {
        yield return new Profile
        {
            Name = "1080p mp4 H.264+AAC",
            IsBuiltIn = true,
            Options = new DownloadOptions
            {
                FormatSelector = "bv*[ext=mp4][vcodec^=avc][height<=1080]+ba[ext=m4a]/b[ext=mp4][height<=1080]/b",
                MergeOutputFormat = "mp4",
                EmbedMetadata = true,
                EmbedThumbnail = true,
            },
        };
        yield return new Profile
        {
            Name = "Best mp4",
            IsBuiltIn = true,
            Options = new DownloadOptions
            {
                FormatSelector = "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/b",
                MergeOutputFormat = "mp4",
                EmbedMetadata = true,
                EmbedThumbnail = true,
            },
        };
        yield return new Profile
        {
            Name = "Audio only m4a",
            IsBuiltIn = true,
            Options = new DownloadOptions
            {
                FormatSelector = "ba[ext=m4a]/ba/b",
                MergeOutputFormat = string.Empty,
                ExtractAudio = true,
                AudioCodec = "m4a",
                AudioQuality = string.Empty,
                EmbedMetadata = true,
                EmbedThumbnail = true,
            },
        };
        yield return new Profile
        {
            Name = "Audio only MP3 320k",
            IsBuiltIn = true,
            Options = new DownloadOptions
            {
                FormatSelector = "ba/b",
                MergeOutputFormat = string.Empty,
                ExtractAudio = true,
                AudioCodec = "mp3",
                AudioQuality = "320",
                EmbedMetadata = true,
                EmbedThumbnail = true,
            },
        };
    }
}
