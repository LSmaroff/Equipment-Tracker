using System.Windows;
using System.Windows.Media;

namespace EquipmentTracking.App.Services;

#pragma warning disable WPF0001
public sealed class ThemeService
{
    private static readonly IReadOnlyDictionary<string, string> LightColors =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = "#F4F7FA",
            ["NavigationBackgroundBrush"] = "#EDF2F7",
            ["SurfaceBrush"] = "#FFFFFF",
            ["SurfaceSecondaryBrush"] = "#F8FAFC",
            ["SurfaceTertiaryBrush"] = "#EAF0F6",
            ["InputSurfaceBrush"] = "#FFFFFF",
            ["HoverSurfaceBrush"] = "#E8EEF5",
            ["BorderNeutralBrush"] = "#D3DCE6",
            ["BorderStrongBrush"] = "#A9B7C6",
            ["TextPrimaryBrush"] = "#132238",
            ["TextSecondaryBrush"] = "#45566C",
            ["TextMutedBrush"] = "#64748B",
            ["TextDisabledBrush"] = "#8391A2",
            ["AccentBrush"] = "#0B5CAD",
            ["AccentFillBrush"] = "#0B5CAD",
            ["AccentHoverBrush"] = "#094E92",
            ["AccentPressedBrush"] = "#073D75",
            ["AccentLightBrush"] = "#E7F2FF",
            ["SelectionBrush"] = "#D7EAFF",
            ["SameTransactionBrush"] = "#EAF4FF",
            ["FocusBrush"] = "#0B5CAD",
            ["SuccessBrush"] = "#19733A",
            ["SuccessSurfaceBrush"] = "#E8F6EC",
            ["WarningBrush"] = "#8A5200",
            ["WarningSurfaceBrush"] = "#FFF3D9",
            ["DangerBrush"] = "#B42318",
            ["DangerSurfaceBrush"] = "#FDECEA",
            ["InfoBrush"] = "#0B5CAD",
            ["InfoSurfaceBrush"] = "#E7F2FF",
            ["OverlayBrush"] = "#7A0B1220",
            ["ButtonTextOnAccentBrush"] = "#FFFFFF"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkColors =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = "#0B1220",
            ["NavigationBackgroundBrush"] = "#111A2B",
            ["SurfaceBrush"] = "#172235",
            ["SurfaceSecondaryBrush"] = "#1E2B3E",
            ["SurfaceTertiaryBrush"] = "#27364A",
            ["InputSurfaceBrush"] = "#111C2E",
            ["HoverSurfaceBrush"] = "#22314A",
            ["BorderNeutralBrush"] = "#33445A",
            ["BorderStrongBrush"] = "#50627A",
            ["TextPrimaryBrush"] = "#F5F7FA",
            ["TextSecondaryBrush"] = "#C1CAD6",
            ["TextMutedBrush"] = "#94A3B8",
            ["TextDisabledBrush"] = "#728197",
            ["AccentBrush"] = "#6CB4FF",
            ["AccentFillBrush"] = "#2563EB",
            ["AccentHoverBrush"] = "#1D4ED8",
            ["AccentPressedBrush"] = "#1E40AF",
            ["AccentLightBrush"] = "#162D4D",
            ["SelectionBrush"] = "#203A5B",
            ["SameTransactionBrush"] = "#1A324D",
            ["FocusBrush"] = "#8AC5FF",
            ["SuccessBrush"] = "#64D17E",
            ["SuccessSurfaceBrush"] = "#173524",
            ["WarningBrush"] = "#F4B860",
            ["WarningSurfaceBrush"] = "#3A2B16",
            ["DangerBrush"] = "#FF817A",
            ["DangerSurfaceBrush"] = "#3B2023",
            ["InfoBrush"] = "#6CB4FF",
            ["InfoSurfaceBrush"] = "#162D4D",
            ["OverlayBrush"] = "#A6000000",
            ["ButtonTextOnAccentBrush"] = "#FFFFFF"
        };

    public IReadOnlyList<string> AvailableThemes { get; } = ["Dark", "Light", "System"];

    public void Apply(string? themeName)
    {
        var normalized = Normalize(themeName);
        var useDarkPalette = normalized == "Dark" ||
                             (normalized == "System" && IsWindowsDarkMode());

        Application.Current.ThemeMode = normalized switch
        {
            "Light" => ThemeMode.Light,
            "System" => ThemeMode.System,
            _ => ThemeMode.Dark
        };

        ApplyColors(useDarkPalette ? DarkColors : LightColors);
    }

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "light" => "Light",
            "system" => "System",
            _ => "Dark"
        };
    }

    private static void ApplyColors(IReadOnlyDictionary<string, string> colors)
    {
        foreach (var pair in colors)
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(pair.Value));
            ReplaceResource(Application.Current.Resources, pair.Key, brush);
        }
    }

    private static bool ReplaceResource(
        ResourceDictionary dictionary,
        string key,
        SolidColorBrush brush)
    {
        if (dictionary.Contains(key))
        {
            dictionary[key] = brush;
            return true;
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            if (ReplaceResource(mergedDictionary, key, brush))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return true;
        }
    }
}
#pragma warning restore WPF0001
