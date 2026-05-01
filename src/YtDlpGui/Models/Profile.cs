using System.Text.Json.Serialization;

namespace YtDlpGui.Models;

/// <summary>
/// SPEC §5.2 mode 1: a saved option preset. Built-ins are immutable; user
/// profiles are persisted to <c>%LOCALAPPDATA%\YtDlpGui\profiles.json</c>.
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Carries the format / post-processing fields. On Apply the consumer copies
    /// this subset onto its current state and (intentionally) preserves the
    /// per-session output folder if this profile's is empty.
    /// </summary>
    public DownloadOptions Options { get; set; } = new();

    /// <summary>Runtime flag, not persisted. True for the four shipping presets.</summary>
    [JsonIgnore] public bool IsBuiltIn { get; set; }

    public override string ToString() => IsBuiltIn ? $"★ {Name}" : Name;
}
