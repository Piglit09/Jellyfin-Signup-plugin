using Jellyfin.Plugin.Signup.Models;

namespace Jellyfin.Plugin.Signup.Services;

/// <summary>
/// Native Jellyfin signup service.
/// </summary>
public interface ISignupService
{
    /// <summary>Validates an invite code.</summary>
    Task<SignupInviteValidationResponse> ValidateInviteAsync(string? inviteCode, string remoteAddress, CancellationToken cancellationToken);

    /// <summary>Creates a Jellyfin user from a valid invite.</summary>
    Task<SignupResponse> CreateUserAsync(SignupCreateUserRequest request, string remoteAddress, CancellationToken cancellationToken);

    /// <summary>Verifies a pending signup email and finalizes Jellyfin user creation.</summary>
    Task<SignupResponse> VerifyEmailAsync(SignupEmailVerificationRequest request, string remoteAddress, CancellationToken cancellationToken);

    /// <summary>Requests a password reset email.</summary>
    Task<SignupResponse> RequestPasswordResetAsync(SignupPasswordResetRequest request, string remoteAddress, CancellationToken cancellationToken);

    /// <summary>Confirms a password reset code and changes the password.</summary>
    Task<SignupResponse> ConfirmPasswordResetAsync(SignupPasswordResetConfirmRequest request, string remoteAddress, CancellationToken cancellationToken);

    /// <summary>Gets public login page signup button settings.</summary>
    SignupLoginButtonPublicResponse GetPublicLoginButton();

    /// <summary>Gets public signup page settings.</summary>
    SignupPublicSettingsResponse GetPublicSettings();

    /// <summary>Gets admin signup and email settings.</summary>
    SignupAdminSettingsResponse GetAdminSettings();

    /// <summary>Saves admin signup and email settings.</summary>
    SignupAdminSettingsResponse SaveAdminSettings(SignupAdminSettingsPatch request);

    /// <summary>Sends a test email using saved settings.</summary>
    Task<SignupResponse> SendTestEmailAsync(SignupTestEmailRequest request, CancellationToken cancellationToken);

    /// <summary>Cleans expired pending verification/reset/rate-limit records.</summary>
    int CleanupExpiredRecords();

    /// <summary>Gets admin invite state.</summary>
    SignupInviteListResponse GetAdminInvites();

    /// <summary>Creates an invite.</summary>
    SignupInviteDto CreateInvite(SignupCreateInviteRequest request, string createdBy);

    /// <summary>Disables an invite.</summary>
    SignupInviteDto DisableInvite(string inviteId);

    /// <summary>Deletes an invite.</summary>
    void DeleteInvite(string inviteId);
}

