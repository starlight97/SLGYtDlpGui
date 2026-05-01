using YtDlpGui.Models;

namespace YtDlpGui.Services;

public interface IProfileStore
{
    /// <summary>Built-ins concatenated with user profiles (built-ins first).</summary>
    IReadOnlyList<Profile> All { get; }

    /// <summary>True if a user profile with this name exists.</summary>
    bool ExistsUserProfile(string name);

    /// <summary>Add or replace a user profile by name (case-insensitive). Built-ins are read-only.</summary>
    void Upsert(Profile profile);

    /// <summary>Remove a user profile by name. No-op for built-ins or unknown names.</summary>
    void Delete(string name);

    /// <summary>Fired after Upsert / Delete so view-models can refresh.</summary>
    event Action? Changed;
}
