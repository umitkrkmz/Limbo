using System.Text.Json;
using System.Text.Json.Serialization;
using Limbo.Core.Bypass;

namespace Limbo.Core.Profiles;

/// <summary>
/// Loads, persists, and looks up per-domain bypass profiles. Each profile is
/// stored as one JSON file under the profiles directory
/// (default: %LOCALAPPDATA%\Limbo\profiles).
/// </summary>
public class ProfileManager
{
    private static readonly string DefaultProfilesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Limbo", "profiles");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;
    private readonly Dictionary<string, DomainProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ProfileManager() : this(DefaultProfilesDirectory)
    {
    }

    public ProfileManager(string profilesDirectory)
    {
        _directory = profilesDirectory;
        Directory.CreateDirectory(_directory);
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<DomainProfile>(json, JsonOptions);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Domain))
                {
                    _profiles[profile.Domain] = profile;
                }
            }
            catch (JsonException)
            {
                // Skip corrupt profile files rather than failing startup
            }
        }
    }

    /// <summary>
    /// Save (create or update) a profile and persist it to disk.
    /// </summary>
    public void SaveProfile(DomainProfile profile)
    {
        lock (_lock)
        {
            _profiles[profile.Domain] = profile;
            var json = JsonSerializer.Serialize(profile, JsonOptions);
            File.WriteAllText(GetFilePath(profile.Domain), json);
        }
    }

    /// <summary>
    /// Remove a profile by exact domain match, both from memory and disk.
    /// </summary>
    public bool DeleteProfile(string domain)
    {
        lock (_lock)
        {
            var removedFromMemory = _profiles.Remove(domain);

            var path = GetFilePath(domain);
            var removedFromDisk = File.Exists(path);
            if (removedFromDisk)
            {
                File.Delete(path);
            }

            return removedFromMemory || removedFromDisk;
        }
    }

    /// <summary>
    /// Look up the profile for a domain. Exact matches take precedence over
    /// wildcard ("*.example.com") matches. Returns a Method=None profile
    /// (never null) when nothing matches.
    /// </summary>
    public DomainProfile GetProfile(string domain)
    {
        lock (_lock)
        {
            if (_profiles.TryGetValue(domain, out var exact))
                return exact;

            DomainProfile? bestMatch = null;
            var bestSuffixLength = -1;

            foreach (var profile in _profiles.Values)
            {
                if (!profile.Domain.StartsWith("*.", StringComparison.Ordinal))
                    continue;

                var suffix = profile.Domain[1..]; // "*.example.com" -> ".example.com"
                // "*.discord.com" also covers discord.com itself - the apex is usually
                // the main host (Discord's API/login), and users write the wildcard meaning both.
                var isSubdomain = domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
                var isApex = domain.Equals(suffix[1..], StringComparison.OrdinalIgnoreCase);
                if (!isSubdomain && !isApex)
                    continue;

                if (suffix.Length > bestSuffixLength)
                {
                    bestMatch = profile;
                    bestSuffixLength = suffix.Length;
                }
            }

            return bestMatch ?? new DomainProfile { Domain = domain, Method = BypassMethod.None };
        }
    }

    /// <summary>
    /// All currently loaded profiles.
    /// </summary>
    public IReadOnlyList<DomainProfile> GetAllProfiles()
    {
        lock (_lock)
        {
            return _profiles.Values.ToList();
        }
    }

    private string GetFilePath(string domain) => Path.Combine(_directory, $"{SanitizeFileName(domain)}.json");

    private static string SanitizeFileName(string domain)
    {
        var name = domain.Replace("*", "_wildcard_");
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }
}
