using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Substratal.Hub.Core.Auth;
using Substratal.Hub.Core.Navigation;
using Substratal.Hub.Core.ViewModels;
using Substratal.Hub.Views;
using Windows.System;

namespace Substratal.Hub;

public sealed partial class MainWindow : Window
{
    ShellPage? shell;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/Substratal.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 840));

        // GLB-01: Ctrl+K and Ctrl+E open the command palette from anywhere.
        foreach (var key in new[] { VirtualKey.K, VirtualKey.E })
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, e) => { e.Handled = true; Palette.Focus(FocusState.Keyboard); };
            Root.KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>AUTH-01, full window with no NavigationView (PLATFORM-SPEC §2).</summary>
    public void ShowSignIn(SessionEndReason? reason)
    {
        shell = null;
        Palette.Visibility = Visibility.Collapsed;
        AppTitleBar.IsBackButtonVisible = false;
        AppTitleBar.IsPaneToggleButtonVisible = false;
        UpdateEnvironmentBadge();
        RootFrame.Navigate(typeof(SignInPage), reason);
    }

    public void ShowShell()
    {
        Palette.Visibility = Visibility.Visible;
        AppTitleBar.IsPaneToggleButtonVisible = true;
        UpdateEnvironmentBadge();
        RootFrame.Navigate(typeof(ShellPage));
        shell = RootFrame.Content as ShellPage;
        if (shell is not null) shell.CanGoBackChanged += (_, canGoBack) => AppTitleBar.IsBackButtonVisible = canGoBack;
    }

    void UpdateEnvironmentBadge()
    {
        var env = App.Services.GetRequiredService<SessionManager>().Environment;
        EnvironmentBadge.Visibility = env.IsProduction ? Visibility.Collapsed : Visibility.Visible;
        EnvironmentText.Text = env.Name.ToUpperInvariant();
    }

    void OnBackRequested(TitleBar sender, object args) => shell?.GoBack();

    void OnPaneToggleRequested(TitleBar sender, object args) => shell?.TogglePane();

    // ── GLB-01 Command palette ─────────────────────────────────────────────

    void OnPaletteTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = PaletteResults(sender.Text).Take(10).ToList();
    }

    void OnPaletteQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var target = args.ChosenSuggestion as PaletteResult ?? PaletteResults(args.QueryText).FirstOrDefault();
        if (target is null || shell is null) return;
        shell.Open(target.ScreenId, target.Parameter);
        sender.Text = "";
        sender.ItemsSource = null;
    }

    /// <summary>
    /// Screens the person can open, by title or ID, plus prefixed IDs routed to their detail screen
    /// (CLIENT-ARCHITECTURE §4: only the prefix is ever interpreted).
    /// </summary>
    IEnumerable<PaletteResult> PaletteResults(string text)
    {
        var q = text.Trim();
        if (q.Length == 0) yield break;

        if (IdRoutes.FirstOrDefault(r => q.StartsWith(r.Prefix, StringComparison.Ordinal)) is { Prefix: not null } route)
            yield return new PaletteResult($"Open {q}", route.ScreenId, q);

        var shellVm = App.Services.GetRequiredService<ShellViewModel>();
        foreach (var s in ScreenCatalog.All.Where(s => s.IsPage && !s.IsOutsideShell && !s.Route.Contains(':') && shellVm.CanOpen(s.Id)))
            if (s.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
                yield return new PaletteResult($"{s.Title}  ·  {s.Id}", s.Id, null);
    }

    static readonly (string Prefix, string ScreenId)[] IdRoutes =
    [
        ("usr_", "USR-03"), ("app_", "APL-03"), ("key_", "KEY-04"), ("role_", "ROL-02"),
        ("evt_", "AUD-02"), ("org_", "ORG-03"), ("whk_", "WHK-03"), ("tnt_", "BIL-04"),
    ];
}

public sealed record PaletteResult(string Label, string ScreenId, string? Parameter)
{
    public override string ToString() => Label;
}
