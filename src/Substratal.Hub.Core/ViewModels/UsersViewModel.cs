using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Substratal.Hub.Core.Api;
using Substratal.Hub.Core.Api.Generated;
using Substratal.Hub.Core.Api.Generated.Models;
using Substratal.Hub.Core.Api.Generated.Users;

namespace Substratal.Hub.Core.ViewModels;

/// <summary>One row of USR-01. Display name comes from the Profile, which the list doesn't include, so it's the email.</summary>
public sealed record UserRow(string Id, string Email, string Status, bool MfaEnabled, bool TestMode, DateTimeOffset? LastLoginAt, DateTimeOffset? CreatedAt)
{
    public string LastLogin => LastLoginAt?.LocalDateTime.ToString("g") ?? "Never";
    public string Created => CreatedAt?.LocalDateTime.ToString("d") ?? "";
}

/// <summary>
/// USR-01 Users: <c>GET /users?limit=50&amp;cursor&amp;status&amp;q</c>, cursor-paged with "Load more" on desktop
/// (CLIENT-ARCHITECTURE §4), search debounced 300 ms, and an exact <c>usr_</c> id jumping straight to USR-03.
/// </summary>
public sealed partial class UsersViewModel(SubstratalApiClient api) : ObservableObject
{
    public const int PageSize = 50;
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    string? cursor;
    CancellationTokenSource? searchDelay;

    public ObservableCollection<UserRow> Users { get; } = [];

    /// <summary>Status chips. Null is "All"; <c>deleted</c> is offered only to users.manage (the view hides it).</summary>
    public IReadOnlyList<string?> StatusFilters { get; } = [null, "active", "invited", "suspended", "deleted"];

    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    /// <summary>Raised with a user id when the search box holds an exact id: the view navigates to USR-03.</summary>
    public event EventHandler<string>? OpenUserRequested;

    partial void OnQueryChanged(string value) => _ = DebouncedReloadAsync();
    partial void OnStatusChanged(string? value) => _ = ReloadAsync();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        cursor = null;
        Users.Clear();
        await LoadPageAsync();
    }

    [RelayCommand]
    public Task LoadMoreAsync() => HasMore && !IsLoading ? LoadPageAsync() : Task.CompletedTask;

    async Task DebouncedReloadAsync()
    {
        searchDelay?.Cancel();
        var cts = searchDelay = new CancellationTokenSource();
        try { await Task.Delay(Debounce, cts.Token); }
        catch (TaskCanceledException) { return; }

        var q = Query.Trim();
        if (q.StartsWith("usr_", StringComparison.Ordinal) && q.Length == 30)
        {
            OpenUserRequested?.Invoke(this, q);
            return;
        }
        await ReloadAsync();
    }

    async Task LoadPageAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var page = await api.Users.GetAsync(r =>
            {
                r.QueryParameters.Limit = PageSize;
                r.QueryParameters.Cursor = cursor;
                var q = Query.Trim();
                if (q.Length > 0) r.QueryParameters.Q = q;
                if (Status is not null && Enum.TryParse<GetStatusQueryParameterType>(Status, ignoreCase: true, out var s))
                    r.QueryParameters.Status = s;
            });
            foreach (var u in page?.Data ?? [])
                Users.Add(new UserRow(u.Id ?? "", u.Email ?? "", u.Status?.ToString().ToLowerInvariant() ?? "", u.MfaEnabled ?? false, u.TestMode ?? false, u.LastLoginAt, u.CreatedAt));
            cursor = page?.Page?.NextCursor;
            HasMore = page?.Page?.HasMore ?? false;
        }
        catch (Error e)
        {
            ErrorMessage = ErrorCopy.For(ApiError.From(e))?.Message;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "You're offline. Showing nothing until you reconnect.";
        }
        finally
        {
            IsLoading = false;
            IsEmpty = Users.Count == 0 && ErrorMessage is null;
        }
    }
}
