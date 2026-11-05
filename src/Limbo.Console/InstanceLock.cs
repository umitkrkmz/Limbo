namespace Limbo.Console;

/// <summary>
/// Cross-process coordination between Limbo copies (and its sister app ZeroDivert).
///
/// Locks here are held by *existence*, not ownership: a named mutex lives as long as
/// some process has a handle to it, and <c>createdNew</c> tells us atomically whether
/// we were first. Mutex ownership would be the wrong tool - it's bound to the
/// acquiring thread, and the run/calibrate/scan paths are async and resume on
/// arbitrary pool threads, where ReleaseMutex throws.
/// </summary>
internal static class InstanceLock
{
    // Global\ so a copy in another session is seen too. Must match AppMutex in
    // installer/Limbo.iss, which refuses to upgrade/uninstall while any copy runs.
    public const string RunningMutexName = @"Global\Limbo.Running";

    // Held only while this process intercepts packets (run, calibration, live scan):
    // two interceptors would both rewrite the same packets. Read-only commands
    // (profiles, config, radar list) don't take it, so they work alongside a running engine.
    private const string InterceptMutexName = @"Global\Limbo.Intercept";

    private const string ZeroDivertMutexName = @"Global\ZeroDivert.SingleInstance";

    private static Mutex? _running;

    public static void MarkRunning()
    {
        try
        {
            _running = new Mutex(initiallyOwned: false, RunningMutexName);
        }
        catch (UnauthorizedAccessException)
        {
            // Created by an elevated copy and we're not elevated (only possible when
            // run as `dotnet Limbo.Console.dll`); the installer check is covered by that copy.
        }
    }

    /// <summary>
    /// Returns a handle to dispose when interception ends, or null if another Limbo
    /// process is already intercepting.
    /// </summary>
    public static IDisposable? TryAcquireInterception()
    {
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: false, InterceptMutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (createdNew) return mutex;

        mutex.Dispose();
        return null;
    }

    public static bool IsZeroDivertRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(ZeroDivertMutexName, out var mutex)) return false;
            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
