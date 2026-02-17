using Microsoft.Win32;
using System.Windows;

namespace Limbo.GUI;

/// <summary>
/// Theme modes available in the application
/// </summary>
public enum LimboThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// Manages application theme switching and Windows system theme synchronization
/// </summary>
public static class ThemeManager
{
    private static LimboThemeMode _currentMode = LimboThemeMode.System;
    private static bool _isWatching;

    /// <summary>
    /// Apply a theme mode to the application
    /// </summary>
    public static void ApplyTheme(LimboThemeMode mode)
    {
        _currentMode = mode;

        var actualTheme = mode == LimboThemeMode.System ? GetSystemTheme() : mode;
        var themePath = actualTheme == LimboThemeMode.Light
            ? "Themes/LightTheme.xaml"
            : "Themes/DarkTheme.xaml";

        var dict = new ResourceDictionary
        {
            Source = new Uri(themePath, UriKind.Relative)
        };

        var mergedDicts = Application.Current.Resources.MergedDictionaries;

        // Remove existing theme dictionary (index 1, after TrayIcon.xaml)
        // Keep the first entry (TrayIcon.xaml) and replace or add the theme
        if (mergedDicts.Count > 1)
        {
            mergedDicts[1] = dict;
        }
        else
        {
            mergedDicts.Add(dict);
        }
    }

    /// <summary>
    /// Detect the current Windows system theme
    /// </summary>
    public static LimboThemeMode GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 1 ? LimboThemeMode.Light : LimboThemeMode.Dark;
                }
            }
        }
        catch
        {
            // Registry not accessible, default to dark
        }

        return LimboThemeMode.Dark;
    }

    /// <summary>
    /// Start watching for Windows theme changes
    /// </summary>
    public static void StartWatching()
    {
        if (_isWatching) return;
        _isWatching = true;

        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    /// <summary>
    /// Stop watching for Windows theme changes
    /// </summary>
    public static void StopWatching()
    {
        if (!_isWatching) return;
        _isWatching = false;

        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
    }

    /// <summary>
    /// Parse a theme mode string from config
    /// </summary>
    public static LimboThemeMode ParseTheme(string? themeName)
    {
        return themeName?.ToLowerInvariant() switch
        {
            "light" => LimboThemeMode.Light,
            "dark" => LimboThemeMode.Dark,
            _ => LimboThemeMode.System
        };
    }

    /// <summary>
    /// Convert theme mode to config string
    /// </summary>
    public static string ThemeToString(LimboThemeMode mode)
    {
        return mode switch
        {
            LimboThemeMode.Light => "Light",
            LimboThemeMode.Dark => "Dark",
            _ => "System"
        };
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_currentMode != LimboThemeMode.System) return;

        // Re-apply system theme
        Application.Current.Dispatcher.BeginInvoke(() => ApplyTheme(LimboThemeMode.System));
    }
}
