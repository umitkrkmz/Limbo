using System.Text.Json;
using Limbo.Core.Config;

namespace Limbo.Console;

internal sealed record UpdateInfo(string LatestTag, string ReleaseUrl);

/// <summary>
/// Looks up the latest published GitHub release once per interactive session, in
/// the background, and only reports it - downloading and installing stays with the
/// user. Started only by the interactive menu, so scripted CLI commands never make
/// this request. The only request is an anonymous GET to the GitHub API. Can be
/// turned off in General Settings (<see cref="LimboConfig.CheckForUpdates"/>).
/// </summary>
internal static class UpdateChecker
{
    public enum CheckStatus { Disabled, Checking, UpToDate, UpdateAvailable, Failed }

    internal sealed record Result(bool Succeeded, UpdateInfo? Update);

    private static Task<Result>? _check;

    public static void StartIfEnabled(LimboConfig config)
    {
        if (_check == null && config.CheckForUpdates)
        {
            _check = CheckAsync(AppInfo.LatestReleaseApiUrl, AppInfo.Version);
        }
    }

    public static void Stop() => _check = null;

    public static CheckStatus Status => _check switch
    {
        null => CheckStatus.Disabled,
        { IsCompleted: false } => CheckStatus.Checking,
        { Result.Succeeded: false } => CheckStatus.Failed,
        { Result.Update: not null } => CheckStatus.UpdateAvailable,
        _ => CheckStatus.UpToDate
    };

    public static UpdateInfo? Available => Status == CheckStatus.UpdateAvailable ? _check!.Result.Update : null;

    internal static async Task<Result> CheckAsync(string apiUrl, Version current)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // GitHub's API rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Limbo/{AppInfo.Version}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            // /releases/latest already skips drafts and pre-releases.
            using var doc = JsonDocument.Parse(await http.GetStringAsync(apiUrl));
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            var url = doc.RootElement.GetProperty("html_url").GetString();

            if (tag == null || url == null || !TryParseTag(tag, out _))
            {
                return new Result(false, null);
            }

            return new Result(true, IsNewer(tag, current) ? new UpdateInfo(tag, url) : null);
        }
        catch
        {
            // Offline, rate-limited or GitHub unreachable: not worth bothering the user.
            return new Result(false, null);
        }
    }

    internal static bool IsNewer(string tag, Version current) =>
        TryParseTag(tag, out var latest) && latest > AppInfo.Normalize(current);

    /// <summary>"v1.2.0" / "v1.2.0-rc.1" -> 1.2.0</summary>
    internal static bool TryParseTag(string tag, out Version version)
    {
        var core = tag.TrimStart('v', 'V').Split('-', '+')[0];
        if (Version.TryParse(core, out var parsed))
        {
            version = AppInfo.Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }
}
