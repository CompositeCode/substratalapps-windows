using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Substratal.Hub.Core.Api;
using Substratal.Hub.Core.Auth;

namespace Substratal.Hub.Core.ViewModels;

public enum SignInStep { Credentials, TwoFactor }

/// <summary>AUTH-01 Sign in and AUTH-02 Two-factor challenge.</summary>
public sealed partial class SignInViewModel(SessionManager session, TimeProvider clock) : ObservableObject
{
    string? mfaToken;
    DateTimeOffset mfaExpiresAt;

    public ObservableCollection<ApiEnvironment> Environments { get; } = new(ApiEnvironment.BuiltIn);

    [ObservableProperty] public partial ApiEnvironment SelectedEnvironment { get; set; } = session.Environment;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Password { get; set; } = "";

    /// <summary>Developer builds only (OPEN-QUESTIONS #13): sign in the test-mode account with this email.</summary>
    [ObservableProperty] public partial bool TestMode { get; set; }

    [ObservableProperty] public partial SignInStep Step { get; set; } = SignInStep.Credentials;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial string Code { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial bool UseRecoveryCode { get; set; }

    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string? RequestId { get; set; }

    /// <summary>Raised with the signed-in user id; the app then builds the shell.</summary>
    public event EventHandler<string>? SignedIn;

    partial void OnSelectedEnvironmentChanged(ApiEnvironment value) => session.UseEnvironment(value);

    bool CanSignIn() => Email.Trim().Length > 0 && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    async Task SignInAsync(CancellationToken cancellationToken)
    {
        ClearError();
        try
        {
            switch (await session.SignInAsync(Email, Password, TestMode, cancellationToken))
            {
                case SignInResult.SignedIn s:
                    Password = "";
                    SignedIn?.Invoke(this, s.UserId);
                    break;
                case SignInResult.MfaRequired m:
                    (mfaToken, mfaExpiresAt) = (m.MfaToken, m.ExpiresAt);
                    Password = "";
                    Step = SignInStep.TwoFactor;
                    break;
            }
        }
        catch (HubApiException e)
        {
            ShowError(e.Error, e.Error.Status == 401 ? "Email or password is incorrect." : null);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "You're offline. Check your connection and try again.";
        }
    }

    bool CanVerify() => UseRecoveryCode ? Code.Trim().Length > 0 : Code.Trim().Length == 6 && Code.Trim().All(char.IsAsciiDigit);

    [RelayCommand(CanExecute = nameof(CanVerify))]
    async Task VerifyAsync(CancellationToken cancellationToken)
    {
        ClearError();
        if (mfaToken is null || clock.GetUtcNow() >= mfaExpiresAt)
        {
            BackToCredentials();
            ErrorMessage = "That sign-in took too long. Sign in again.";
            return;
        }
        try
        {
            var code = Code.Trim();
            var result = await session.VerifyMfaAsync(mfaToken, UseRecoveryCode ? null : code, UseRecoveryCode ? code : null, cancellationToken);
            mfaToken = null;
            Code = "";
            SignedIn?.Invoke(this, result.UserId);
        }
        catch (HubApiException e)
        {
            ShowError(e.Error, e.Error.Status == 401 ? "That code didn't work. Check your authenticator and try again." : null);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "You're offline. Check your connection and try again.";
        }
    }

    [RelayCommand]
    void BackToCredentials()
    {
        mfaToken = null;
        Code = "";
        UseRecoveryCode = false;
        Step = SignInStep.Credentials;
    }

    void ShowError(ApiError error, string? overrideMessage)
    {
        RequestId = error.RequestId;
        ErrorMessage = overrideMessage ?? ErrorCopy.For(error)?.Message ?? error.Message;
    }

    void ClearError() => (ErrorMessage, RequestId) = (null, null);
}
