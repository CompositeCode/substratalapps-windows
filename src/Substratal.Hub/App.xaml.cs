using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Substratal.Hub.Core;
using Substratal.Hub.Core.Auth;
using Substratal.Hub.Core.Capabilities;
using Substratal.Hub.Core.ViewModels;
using Substratal.Hub.Services;
using Substratal.Hub.Theming;

namespace Substratal.Hub;

public partial class App : Application
{
    readonly IHost host;
    MainWindow? window;

    public App()
    {
        InitializeComponent();
        host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSubstratalHubCore(new HubClientInfo("Windows", AppInfo.Version, RuntimeInformation.OSDescription));
                services.AddSingleton<ITokenStore, CredentialLockerTokenStore>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<AppPreferences>();
                services.AddTransient<SignInViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddTransient<UsersViewModel>();
            })
            .Build();
    }

    public static App CurrentApp => (App)Current;
    public static IServiceProvider Services => CurrentApp.host.Services;
    public static MainWindow MainWindow => CurrentApp.window ?? throw new InvalidOperationException("The window isn't created yet.");

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var main = window = new MainWindow();
        // Apply the saved theme before first paint (CLIENT-ARCHITECTURE §13).
        var preferences = Services.GetRequiredService<AppPreferences>();
        if (main.Content is FrameworkElement root)
            Services.GetRequiredService<ThemeService>().Apply(root, preferences.ThemeMode, preferences.LightTheme, preferences.DarkTheme);
        main.Activate();

        // Restore the last environment's session before the shell exists (CLIENT-ARCHITECTURE §3). The sign-in page
        // shows while this runs, so a slow network never leaves a blank window.
        var session = Services.GetRequiredService<SessionManager>();
        session.UseEnvironment(preferences.Environment);
        session.Ended += (_, reason) => main.DispatcherQueue.TryEnqueue(() =>
        {
            Services.GetRequiredService<CapabilitiesService>().Clear();
            main.ShowSignIn(reason);
        });

        main.ShowSignIn(null);
        try
        {
            if (await session.TryRestoreAsync())
                main.ShowShell();
        }
        catch (HttpRequestException)
        {
            // Offline at launch: stay on sign-in. GLB-03's offline banner covers the shell once signed in.
        }
    }
}

/// <summary>Version and identity of this build.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
