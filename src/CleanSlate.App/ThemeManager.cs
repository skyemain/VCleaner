using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CleanSlate.App;

/// <summary>Swaps the light and dark resource dictionaries. Follows the Windows app theme unless the user opted out.</summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static bool _followSystem = true;
    private static bool _started;

    public static bool IsDark { get; private set; }

    public static event EventHandler<bool>? ThemeChanged;

    public static void Start(bool followSystem)
    {
        _followSystem = followSystem;
        if (!_started)
        {
            _started = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                {
                    Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
                }
            };
        }

        Apply();
    }

    public static void SetFollowSystem(bool followSystem)
    {
        _followSystem = followSystem;
        Apply();
    }

    private static void Apply()
    {
        var dark = _followSystem && SystemUsesDarkTheme();
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var merged = app.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source is not null && d.Source.OriginalString.StartsWith("Themes/Light", StringComparison.Ordinal) ||
                                                  d.Source is not null && d.Source.OriginalString.StartsWith("Themes/Dark", StringComparison.Ordinal));
        var replacement = new ResourceDictionary { Source = uri };
        if (existing is null)
        {
            merged.Insert(0, replacement);
        }
        else
        {
            merged[merged.IndexOf(existing)] = replacement;
        }

        if (dark != IsDark)
        {
            IsDark = dark;
            ThemeChanged?.Invoke(null, dark);
        }
        else
        {
            IsDark = dark;
        }
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
