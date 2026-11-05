using System.Reflection;

namespace Limbo.Console;

internal static class AppInfo
{
    public const string RepoUrl = "https://github.com/umitkrkmz/Limbo";
    public const string License = "GPL-3.0-or-later";

    /// <summary>
    /// Major.Minor.Patch from the csproj &lt;Version&gt;, so it never drifts from the build.
    /// </summary>
    public static Version Version { get; } = Normalize(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string VersionText => $"v{Version}";

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
