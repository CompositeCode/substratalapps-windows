using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Substratal.Hub.Core.Api.Generated.Models;
using Substratal.Hub.Core.Help;
using Substratal.Hub.Core.Navigation;
using Substratal.Hub.Core.ViewModels;
using Windows.System;

namespace Substratal.Hub.Views;

/// <summary>Where a screen opens, and with what (a record id for drill-ins).</summary>
public sealed record ScreenTarget(string ScreenId, string? Parameter);

/// <summary>The signed-in shell (PLATFORM-SPEC §2).</summary>
public sealed partial class ShellPage : Page
{
    /// <summary>Screens with a real page in this build. Everything else opens <see cref="ScreenPage"/>.</summary>
    static readonly Dictionary<string, Type> Pages = new(StringComparer.Ordinal)
    {
        ["USR-01"] = typeof(UsersPage),
        ["SET-01"] = typeof(SettingsPage),
    };

    public ShellViewModel ViewModel { get; } = App.Services.GetRequiredService<ShellViewModel>();

    public event EventHandler<bool>? CanGoBackChanged;

    public ShellPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Loaded += OnLoaded;

        var f1 = new KeyboardAccelerator { Key = VirtualKey.F1 };
        f1.Invoked += (_, e) => { e.Handled = true; OnHelp(this, new RoutedEventArgs()); };
        KeyboardAccelerators.Add(f1);
        AddAccelerator(VirtualKey.F5, VirtualKeyModifiers.None, Refresh);
        AddAccelerator(VirtualKey.R, VirtualKeyModifiers.Control, Refresh);
        AddAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu, () => GoBack());
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildMenu();
        UpdateBanners();
        Open(NavigationModel.Landing, null);
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Error)
        {
            // Capabilities stay at "member"; the server still enforces every permission.
        }
        catch (HttpRequestException)
        {
        }
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Personal) or nameof(ShellViewModel.Administration) or nameof(ShellViewModel.Developer) or nameof(ShellViewModel.Footer))
            BuildMenu();
        if (e.PropertyName is nameof(ShellViewModel.VersionBanner) or nameof(ShellViewModel.RequiresTwoFactorSetup))
            UpdateBanners();
    }

    /// <summary>Group headers Personal · Administration · Developer, each shown only when it has items (IA §2).</summary>
    void BuildMenu()
    {
        Nav.MenuItems.Clear();
        Nav.FooterMenuItems.Clear();
        AddSection("Personal", ViewModel.Personal);
        AddSection("Administration", ViewModel.Administration);
        AddSection("Developer", ViewModel.Developer);
        foreach (var item in ViewModel.Footer) Nav.FooterMenuItems.Add(MenuItem(item));
        SelectCurrent();
    }

    void AddSection(string header, IReadOnlyList<NavItem> items)
    {
        if (items.Count == 0) return;
        Nav.MenuItems.Add(new NavigationViewItemHeader { Content = header });
        foreach (var item in items) Nav.MenuItems.Add(MenuItem(item));
    }

    NavigationViewItem MenuItem(NavItem item)
    {
        var view = new NavigationViewItem
        {
            Content = item.Label,
            Tag = item.ScreenId,
            Icon = new FontIcon { Glyph = item.Glyph },
        };
        if (TryParseShortcut(item.Shortcut, out var key, out var modifiers))
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) => { e.Handled = true; Open(item.ScreenId, null); };
            view.KeyboardAccelerators.Add(accelerator);
            ToolTipService.SetToolTip(view, $"{item.Label} ({item.Shortcut})");
        }
        return view;
    }

    void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string id) Open(id, null);
    }

    /// <summary>Opens a screen by id. Used by the menu, the palette, deep links and drill-ins.</summary>
    public void Open(string screenId, string? parameter)
    {
        var page = Pages.GetValueOrDefault(screenId, typeof(ScreenPage));
        ContentFrame.Navigate(page, new ScreenTarget(screenId, parameter));
    }

    public void GoBack()
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    public void TogglePane() => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    void Refresh()
    {
        if (ContentFrame.Content is IRefreshable r) r.Refresh();
    }

    void OnNavigated(object sender, NavigationEventArgs e)
    {
        CanGoBackChanged?.Invoke(this, ContentFrame.CanGoBack);
        if (e.Parameter is not ScreenTarget target) return;
        var screen = ScreenCatalog.Get(target.ScreenId);
        PageTitle.Text = screen.Title;
        Crumbs.ItemsSource = target.Parameter is null
            ? Array.Empty<string>()
            : new[] { SectionTitle(screen), target.Parameter };
        SelectCurrent();
    }

    static string SectionTitle(Screen screen) =>
        NavigationModel.Items.FirstOrDefault(i => i.Screen.Domain == screen.Domain)?.Label ?? screen.DomainTitle;

    void OnCrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index == 0) GoBack();
    }

    /// <summary>The screen on display, as the page reports it.</summary>
    ScreenTarget? Current => (ContentFrame.Content as IScreenPage)?.Target;

    void SelectCurrent()
    {
        if (Current is not { } target) return;
        var items = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().ToList();
        var domain = ScreenCatalog.Get(target.ScreenId).Domain;
        // A drill-in (USR-03) highlights its section's item (Users).
        Nav.SelectedItem = items.FirstOrDefault(i => i.Tag as string == target.ScreenId)
            ?? items.FirstOrDefault(i => i.Tag is string id && ScreenCatalog.Get(id).Domain == domain);
    }

    void UpdateBanners()
    {
        VersionBar.IsOpen = ViewModel.VersionBanner is not null;
        VersionBar.Message = ViewModel.VersionBanner?.Message ?? "";
        VersionBar.IsClosable = ViewModel.VersionBanner is { Blocking: false };
        TwoFactorBar.IsOpen = ViewModel.RequiresTwoFactorSetup;
    }

    async void OnHelp(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(Current is { } t ? HelpLinks.ForScreen(t.ScreenId) : HelpLinks.WindowsApp);

    // GLB-03 Update: the App Installer feed offers the new version (docs/RELEASING.md).
    async void OnUpdate(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(HelpLinks.WindowsApp);

    void OnTurnOnTwoFactor(object sender, RoutedEventArgs e) => Open("ACC-05", null);

    void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) => { e.Handled = true; action(); };
        KeyboardAccelerators.Add(accelerator);
    }

    /// <summary>"Ctrl+Shift+1" → (Number1, Control|Shift).</summary>
    internal static bool TryParseShortcut(string shortcut, out VirtualKey key, out VirtualKeyModifiers modifiers)
    {
        key = VirtualKey.None;
        modifiers = VirtualKeyModifiers.None;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        foreach (var part in shortcut.Split('+'))
        {
            switch (part)
            {
                case "Ctrl": modifiers |= VirtualKeyModifiers.Control; break;
                case "Shift": modifiers |= VirtualKeyModifiers.Shift; break;
                case "Alt": modifiers |= VirtualKeyModifiers.Menu; break;
                case [var c] when char.IsAsciiDigit(c): key = VirtualKey.Number0 + (c - '0'); break;
                case [var c] when char.IsAsciiLetterUpper(c): key = VirtualKey.A + (c - 'A'); break;
                default: return false;
            }
        }
        return key != VirtualKey.None;
    }
}

/// <summary>A page that knows which screen it shows, for help links and nav selection.</summary>
public interface IScreenPage
{
    ScreenTarget? Target { get; }
}

/// <summary>A page that reloads on F5 / Ctrl+R.</summary>
public interface IRefreshable
{
    void Refresh();
}
