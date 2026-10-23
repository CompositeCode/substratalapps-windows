using Substratal.Hub.Core.Api;
using Substratal.Hub.Theming;
using Windows.Storage;

namespace Substratal.Hub.Services;

/// <summary>
/// Device-local, non-secret preferences in the package's LocalSettings: the last environment and the theme choice.
/// The theme also belongs in <c>app_hub</c> settings once its schema has the keys (OPEN-QUESTIONS #22); until then it
/// stays on the device, as the spec allows.
/// </summary>
public sealed class AppPreferences
{
    readonly ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;

    public ApiEnvironment Environment
    {
        get
        {
            var id = settings.Values["environment"] as string;
            if (id == "custom" && Uri.TryCreate(settings.Values["environment.url"] as string, UriKind.Absolute, out var url))
                return ApiEnvironment.Custom(url);
            return ApiEnvironment.BuiltIn.FirstOrDefault(e => e.Id == id) ?? ApiEnvironment.Production;
        }
        set
        {
            settings.Values["environment"] = value.Id;
            settings.Values["environment.url"] = value.BaseUrl.ToString();
        }
    }

    public ThemeMode ThemeMode
    {
        get => Enum.TryParse<ThemeMode>(settings.Values["theme.mode"] as string, out var m) ? m : ThemeMode.System;
        set => settings.Values["theme.mode"] = value.ToString();
    }

    public LightTheme LightTheme
    {
        get => ThemeService.Parse(settings.Values["theme.light"] as string, ThemeService.DefaultLight);
        set => settings.Values["theme.light"] = ThemeService.Id(value);
    }

    public DarkTheme DarkTheme
    {
        get => ThemeService.Parse(settings.Values["theme.dark"] as string, ThemeService.DefaultDark);
        set => settings.Values["theme.dark"] = ThemeService.Id(value);
    }
}
