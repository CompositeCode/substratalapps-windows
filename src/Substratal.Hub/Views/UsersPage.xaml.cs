using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Substratal.Hub.Core.ViewModels;

namespace Substratal.Hub.Views;

/// <summary>USR-01 Users: <c>GET /users</c>, cursor-paged.</summary>
public sealed partial class UsersPage : Page, IScreenPage, IRefreshable
{
    public UsersViewModel ViewModel { get; } = App.Services.GetRequiredService<UsersViewModel>();
    public ScreenTarget? Target { get; private set; }

    public UsersPage()
    {
        InitializeComponent();
        ViewModel.OpenUserRequested += (_, id) => OpenUser(id);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Target = e.Parameter as ScreenTarget;
        var shell = App.Services.GetRequiredService<ShellViewModel>();

        // Deleted accounts are offered only to users.manage; Invite needs it too (SCREEN-SPECS USR-01).
        StatusBox.ItemsSource = ViewModel.StatusFilters
            .Where(s => s != "deleted" || shell.Capabilities.CanManageUsers)
            .Select(s => s is null ? "All statuses" : char.ToUpperInvariant(s[0]) + s[1..])
            .ToList();
        StatusBox.SelectedIndex = 0;
        InviteButton.IsEnabled = shell.Capabilities.CanManageUsers;
        if (!InviteButton.IsEnabled) ToolTipService.SetToolTip(InviteButton, "You need the users.manage permission to invite people.");

        if (ViewModel.Users.Count == 0) await ViewModel.ReloadAsync();
    }

    public void Refresh() => ViewModel.ReloadCommand.Execute(null);

    void OnStatusChanged(object sender, SelectionChangedEventArgs e)
    {
        var label = StatusBox.SelectedItem as string;
        var status = label is null or "All statuses" ? null : label.ToLowerInvariant();
        if (status != ViewModel.Status) ViewModel.Status = status;
    }

    void OnUserClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is UserRow row) OpenUser(row.Id);
    }

    void OnInvite(object sender, RoutedEventArgs e) => Shell?.Open("USR-02", null);

    void OpenUser(string id) => Shell?.Open("USR-03", id);

    ShellPage? Shell => FindShell(this);

    static ShellPage? FindShell(DependencyObject start)
    {
        for (var node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(start); node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is ShellPage shell) return shell;
        return null;
    }
}
