using System.Text.Json.Serialization;
using Jellyfin.Plugin.Signup.Configuration;

namespace Jellyfin.Plugin.Signup.Models;

/// <summary>
/// Public native signup invite DTO.
/// </summary>
public sealed class SignupInviteDto
{
    /// <summary>Gets or sets invite id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets optional one-time clear code returned only after creation.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Gets or sets invite code preview.</summary>
    [JsonPropertyName("codePreview")]
    public string CodePreview { get; set; } = string.Empty;

    /// <summary>Gets or sets label.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets created timestamp.</summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets expiration timestamp.</summary>
    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets max uses.</summary>
    [JsonPropertyName("maxUses")]
    public int MaxUses { get; set; }

    /// <summary>Gets or sets used count.</summary>
    [JsonPropertyName("usedCount")]
    public int UsedCount { get; set; }

    /// <summary>Gets or sets whether disabled.</summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    /// <summary>Gets or sets whether email is required.</summary>
    [JsonPropertyName("emailRequired")]
    public bool EmailRequired { get; set; }

    /// <summary>Gets or sets selected policy preset id.</summary>
    [JsonPropertyName("policyPresetId")]
    public string PolicyPresetId { get; set; } = "standard";

    /// <summary>Gets or sets explicit enabled library folder ids.</summary>
    [JsonPropertyName("enabledFolderIds")]
    public List<string> EnabledFolderIds { get; set; } = [];

    /// <summary>Gets or sets whether invite is currently usable.</summary>
    [JsonPropertyName("usable")]
    public bool Usable { get; set; }

    /// <summary>Gets or sets temporary lockout timestamp.</summary>
    [JsonPropertyName("lockedUntilUtc")]
    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>Gets or sets usage records.</summary>
    [JsonPropertyName("usageRecords")]
    public List<SignupInviteUsageRecord> UsageRecords { get; set; } = [];

    /// <summary>
    /// Creates a public invite DTO.
    /// </summary>
    /// <param name="invite">Stored invite.</param>
    /// <param name="clearCode">Optional clear code.</param>
    /// <returns>Public invite DTO.</returns>
    public static SignupInviteDto FromInvite(SignupInvite invite, string? clearCode = null)
    {
        var now = DateTimeOffset.UtcNow;
        var usedCount = invite.UsageRecords?.Count ?? 0;
        var expired = invite.ExpiresAtUtc is not null && invite.ExpiresAtUtc <= now;
        var overUsed = invite.MaxUses > 0 && usedCount >= invite.MaxUses;

        return new SignupInviteDto
        {
            Code = clearCode,
            CodePreview = invite.CodePreview,
            CreatedAtUtc = invite.CreatedAtUtc,
            Disabled = invite.Disabled,
            EmailRequired = invite.EmailRequired,
            EnabledFolderIds = invite.EnabledFolderIds,
            ExpiresAtUtc = invite.ExpiresAtUtc,
            Id = invite.Id,
            Label = invite.Label,
            LockedUntilUtc = invite.LockedUntilUtc,
            MaxUses = invite.MaxUses,
            PolicyPresetId = invite.PolicyPresetId,
            Usable = !invite.Disabled && !expired && !overUsed && (invite.LockedUntilUtc is null || invite.LockedUntilUtc <= now),
            UsageRecords = invite.UsageRecords?.ToList() ?? [],
            UsedCount = usedCount
        };
    }
}

/// <summary>
/// Signup policy preset DTO.
/// </summary>
public sealed class SignupPolicyPresetDto
{
    /// <summary>Gets or sets preset id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "standard";

    /// <summary>Gets or sets preset name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Standard User";

    /// <summary>Gets or sets whether all folders are enabled.</summary>
    [JsonPropertyName("enableAllFolders")]
    public bool EnableAllFolders { get; set; }

    /// <summary>Gets or sets enabled folder ids.</summary>
    [JsonPropertyName("enabledFolderIds")]
    public List<string> EnabledFolderIds { get; set; } = [];

    /// <summary>Gets or sets whether downloads are enabled.</summary>
    [JsonPropertyName("allowDownloads")]
    public bool AllowDownloads { get; set; }

    /// <summary>Gets or sets whether Live TV is enabled.</summary>
    [JsonPropertyName("allowLiveTv")]
    public bool AllowLiveTv { get; set; }

    /// <summary>Gets or sets whether remote control is enabled.</summary>
    [JsonPropertyName("allowRemoteControl")]
    public bool AllowRemoteControl { get; set; }

    /// <summary>Gets or sets whether transcoding is enabled.</summary>
    [JsonPropertyName("allowTranscoding")]
    public bool AllowTranscoding { get; set; }

    /// <summary>
    /// Creates a preset DTO.
    /// </summary>
    /// <param name="preset">Preset.</param>
    /// <returns>DTO.</returns>
    public static SignupPolicyPresetDto FromPreset(SignupPolicyPreset preset) => new()
    {
        AllowDownloads = preset.AllowDownloads,
        AllowLiveTv = preset.AllowLiveTv,
        AllowRemoteControl = preset.AllowRemoteControl,
        AllowTranscoding = preset.AllowTranscoding,
        EnabledFolderIds = preset.EnabledFolderIds,
        EnableAllFolders = preset.EnableAllFolders,
        Id = preset.Id,
        Name = preset.Name
    };
}

/// <summary>
/// Public signup email settings DTO. SMTP password is write-only.
/// </summary>
public sealed class SignupEmailSettingsDto
{
    /// <summary>Gets or sets whether email delivery is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Gets or sets SMTP host.</summary>
    [JsonPropertyName("smtpHost")]
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>Gets or sets SMTP port.</summary>
    [JsonPropertyName("smtpPort")]
    public int SmtpPort { get; set; } = 587;

    /// <summary>Gets or sets whether SSL/TLS is enabled.</summary>
    [JsonPropertyName("useSsl")]
    public bool UseSsl { get; set; } = true;

    /// <summary>Gets or sets SMTP username.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Gets or sets SMTP password when saving settings. Never returned by the backend.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>Gets or sets whether a password is configured.</summary>
    [JsonPropertyName("passwordConfigured")]
    public bool PasswordConfigured { get; set; }

    /// <summary>Gets or sets sender email address.</summary>
    [JsonPropertyName("fromAddress")]
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets sender display name.</summary>
    [JsonPropertyName("fromName")]
    public string FromName { get; set; } = "Jellyfin";

    /// <summary>Gets or sets public logo URL used in branded verification emails.</summary>
    [JsonPropertyName("logoUrl")]
    public string LogoUrl { get; set; } = SignupEmailSettings.DefaultLogoUrl;

    /// <summary>Gets or sets the public signup URL used for email verification links.</summary>
    [JsonPropertyName("publicSignupUrl")]
    public string PublicSignupUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets password reset subject.</summary>
    [JsonPropertyName("resetSubject")]
    public string ResetSubject { get; set; } = "Your Jellyfin password reset code";

    /// <summary>Gets or sets reset code lifetime in minutes.</summary>
    [JsonPropertyName("resetCodeMinutes")]
    public int ResetCodeMinutes { get; set; } = 15;

    /// <summary>Gets or sets email verification subject.</summary>
    [JsonPropertyName("verificationSubject")]
    public string VerificationSubject { get; set; } = "Verify your Jellyfin email";

    /// <summary>Gets or sets email verification link lifetime in minutes.</summary>
    [JsonPropertyName("verificationLinkMinutes")]
    public int VerificationLinkMinutes { get; set; } = 30;

    /// <summary>
    /// Creates a redacted DTO from stored settings.
    /// </summary>
    /// <param name="settings">Stored settings.</param>
    /// <returns>Redacted DTO.</returns>
    public static SignupEmailSettingsDto FromSettings(SignupEmailSettings settings) => new()
    {
        Enabled = settings.Enabled,
        FromAddress = settings.FromAddress,
        FromName = settings.FromName,
        LogoUrl = settings.LogoUrl,
        PasswordConfigured = !string.IsNullOrWhiteSpace(settings.Password),
        PublicSignupUrl = settings.PublicSignupUrl,
        ResetCodeMinutes = settings.ResetCodeMinutes,
        ResetSubject = settings.ResetSubject,
        SmtpHost = settings.SmtpHost,
        SmtpPort = settings.SmtpPort,
        UseSsl = settings.UseSsl,
        Username = settings.Username,
        VerificationLinkMinutes = settings.VerificationLinkMinutes,
        VerificationSubject = settings.VerificationSubject
    };
}

/// <summary>
/// Login page signup button settings DTO.
/// </summary>
public sealed class SignupLoginButtonSettingsDto
{
    /// <summary>Gets or sets whether compatible Jellyfin login pages should show a signup button.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the signup button label.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "Create Account";

    /// <summary>Gets or sets an optional custom signup target URL.</summary>
    [JsonPropertyName("targetUrl")]
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>
    /// Creates a login button DTO.
    /// </summary>
    /// <param name="settings">Stored settings.</param>
    /// <returns>DTO.</returns>
    public static SignupLoginButtonSettingsDto FromSettings(SignupLoginButtonSettings settings) => new()
    {
        Enabled = settings.Enabled,
        TargetUrl = settings.TargetUrl,
        Text = settings.Text
    };
}

/// <summary>
/// Public signup page appearance settings DTO.
/// </summary>
public sealed class SignupAppearanceSettingsDto
{
    /// <summary>Gets or sets the browser title for the public signup page.</summary>
    [JsonPropertyName("pageTitle")]
    public string PageTitle { get; set; } = "Jellyfin Signup";

    /// <summary>Gets or sets the public page heading.</summary>
    [JsonPropertyName("heading")]
    public string Heading { get; set; } = "Jellyfin Signup";

    /// <summary>Gets or sets the public page intro text.</summary>
    [JsonPropertyName("introText")]
    public string IntroText { get; set; } = "Create your Jellyfin account, then verify your email before signing in.";

    /// <summary>Gets or sets optional public logo image URL.</summary>
    [JsonPropertyName("logoUrl")]
    public string LogoUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets page background color.</summary>
    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#08070d";

    /// <summary>Gets or sets signup panel color.</summary>
    [JsonPropertyName("panelColor")]
    public string PanelColor { get; set; } = "#15131d";

    /// <summary>Gets or sets primary text color.</summary>
    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = "#f7f7fb";

    /// <summary>Gets or sets secondary text color.</summary>
    [JsonPropertyName("mutedTextColor")]
    public string MutedTextColor { get; set; } = "#bbb6c8";

    /// <summary>Gets or sets primary accent color.</summary>
    [JsonPropertyName("primaryColor")]
    public string PrimaryColor { get; set; } = "#00a4dc";

    /// <summary>Gets or sets secondary accent color.</summary>
    [JsonPropertyName("secondaryColor")]
    public string SecondaryColor { get; set; } = "#aa5cc3";

    /// <summary>
    /// Creates an appearance DTO.
    /// </summary>
    /// <param name="settings">Stored settings.</param>
    /// <returns>DTO.</returns>
    public static SignupAppearanceSettingsDto FromSettings(SignupAppearanceSettings settings) => new()
    {
        BackgroundColor = settings.BackgroundColor,
        Heading = settings.Heading,
        IntroText = settings.IntroText,
        LogoUrl = settings.LogoUrl,
        MutedTextColor = settings.MutedTextColor,
        PageTitle = settings.PageTitle,
        PanelColor = settings.PanelColor,
        PrimaryColor = settings.PrimaryColor,
        SecondaryColor = settings.SecondaryColor,
        TextColor = settings.TextColor
    };
}

/// <summary>
/// Native signup user DTO.
/// </summary>
public sealed class SignupUserDto
{
    /// <summary>Gets or sets user id.</summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets username.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets email.</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Jellyfin has an email saved for this Jellyfin user.</summary>
    [JsonPropertyName("emailConfigured")]
    public bool EmailConfigured { get; set; }

    /// <summary>Gets or sets policy preset id.</summary>
    [JsonPropertyName("policyPresetId")]
    public string PolicyPresetId { get; set; } = "standard";

    /// <summary>Gets or sets where the mapping came from.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "jellyfin";

    /// <summary>Gets or sets created timestamp.</summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Creates a user DTO.
    /// </summary>
    /// <param name="record">Stored user record.</param>
    /// <returns>DTO.</returns>
    public static SignupUserDto FromRecord(SignupUserRecord record) => new()
    {
        CreatedAtUtc = record.CreatedAtUtc,
        Email = record.Email,
        EmailConfigured = !string.IsNullOrWhiteSpace(record.Email),
        PolicyPresetId = record.PolicyPresetId,
        Source = "signup",
        UserId = record.UserId,
        Username = record.Username
    };
}

/// <summary>
/// Admin patch for a Jellyfin user's Jellyfin email mapping.
/// </summary>
public sealed class SignupUserEmailPatch
{
    /// <summary>Gets or sets Jellyfin user id.</summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets email address. Blank clears the mapping.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

/// <summary>
/// Validate invite request.
/// </summary>
public sealed class SignupValidateInviteRequest
{
    /// <summary>Gets or sets invite code.</summary>
    [JsonPropertyName("inviteCode")]
    public string? InviteCode { get; set; }
}

/// <summary>
/// Create user request.
/// </summary>
public sealed class SignupCreateUserRequest
{
    /// <summary>Gets or sets invite code.</summary>
    [JsonPropertyName("inviteCode")]
    public string? InviteCode { get; set; }

    /// <summary>Gets or sets username.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Gets or sets password.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>Gets or sets confirm password.</summary>
    [JsonPropertyName("confirmPassword")]
    public string? ConfirmPassword { get; set; }

    /// <summary>Gets or sets required email.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Gets or sets verification link base URL.</summary>
    [JsonPropertyName("verificationBaseUrl")]
    public string? VerificationBaseUrl { get; set; }
}

/// <summary>
/// Email verification request.
/// </summary>
public sealed class SignupEmailVerificationRequest
{
    /// <summary>Gets or sets email verification token.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

/// <summary>
/// Password reset request.
/// </summary>
public sealed class SignupPasswordResetRequest
{
    /// <summary>Gets or sets account email.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

/// <summary>
/// Password reset confirmation request.
/// </summary>
public sealed class SignupPasswordResetConfirmRequest
{
    /// <summary>Gets or sets account email.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Gets or sets reset code.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Gets or sets new password.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>Gets or sets confirmed new password.</summary>
    [JsonPropertyName("confirmPassword")]
    public string? ConfirmPassword { get; set; }
}

/// <summary>
/// Create invite request.
/// </summary>
public sealed class SignupCreateInviteRequest
{
    /// <summary>Gets or sets label.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets expiration timestamp.</summary>
    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets max uses.</summary>
    [JsonPropertyName("maxUses")]
    public int? MaxUses { get; set; }

    /// <summary>Gets or sets policy preset id.</summary>
    [JsonPropertyName("policyPresetId")]
    public string? PolicyPresetId { get; set; }

    /// <summary>Gets or sets whether email is required.</summary>
    [JsonPropertyName("emailRequired")]
    public bool? EmailRequired { get; set; }

    /// <summary>Gets or sets explicit enabled folder ids.</summary>
    [JsonPropertyName("enabledFolderIds")]
    public List<string>? EnabledFolderIds { get; set; }
}

/// <summary>
/// Generic signup response.
/// </summary>
public class SignupResponse
{
    /// <summary>Gets or sets whether request succeeded.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>Gets or sets friendly message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets optional user id.</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>Gets or sets optional username.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

/// <summary>
/// Invite validation response.
/// </summary>
public sealed class SignupInviteValidationResponse : SignupResponse
{
    /// <summary>Gets or sets whether invite is valid.</summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    /// <summary>Gets or sets whether email is required.</summary>
    [JsonPropertyName("emailRequired")]
    public bool EmailRequired { get; set; }

    /// <summary>Gets or sets invite label.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets policy preset name.</summary>
    [JsonPropertyName("policyPresetName")]
    public string? PolicyPresetName { get; set; }
}

/// <summary>
/// Invite list response.
/// </summary>
public sealed class SignupInviteListResponse
{
    /// <summary>Gets or sets whether request succeeded.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>Gets or sets whether native signup is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether a valid invite code is required.</summary>
    [JsonPropertyName("requireInvite")]
    public bool RequireInvite { get; set; } = true;

    /// <summary>Gets or sets invites.</summary>
    [JsonPropertyName("invites")]
    public List<SignupInviteDto> Invites { get; set; } = [];

    /// <summary>Gets or sets policy presets.</summary>
    [JsonPropertyName("policyPresets")]
    public List<SignupPolicyPresetDto> PolicyPresets { get; set; } = [];
}

/// <summary>
/// Public login page signup button response.
/// </summary>
public sealed class SignupLoginButtonPublicResponse
{
    /// <summary>Gets or sets whether request succeeded.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>Gets or sets whether the login page signup button should be visible.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the signup button label.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "Create Account";

    /// <summary>Gets or sets an optional custom signup target URL.</summary>
    [JsonPropertyName("targetUrl")]
    public string TargetUrl { get; set; } = string.Empty;
}

/// <summary>
/// Public signup page settings response.
/// </summary>
public sealed class SignupPublicSettingsResponse
{
    /// <summary>Gets or sets whether request succeeded.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>Gets or sets whether native signup is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Gets or sets whether a valid invite code is required.</summary>
    [JsonPropertyName("requireInvite")]
    public bool RequireInvite { get; set; } = true;

    /// <summary>Gets or sets whether email verification and reset are configured.</summary>
    [JsonPropertyName("emailEnabled")]
    public bool EmailEnabled { get; set; }

    /// <summary>Gets or sets public page appearance settings.</summary>
    [JsonPropertyName("appearanceSettings")]
    public SignupAppearanceSettingsDto AppearanceSettings { get; set; } = new();
}

/// <summary>
/// Native signup admin settings response.
/// </summary>
public sealed class SignupAdminSettingsResponse
{
    /// <summary>Gets or sets whether request succeeded.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>Gets or sets whether native signup is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether a valid invite code is required.</summary>
    [JsonPropertyName("requireInvite")]
    public bool RequireInvite { get; set; } = true;

    /// <summary>Gets or sets default policy preset id.</summary>
    [JsonPropertyName("defaultPolicyPresetId")]
    public string DefaultPolicyPresetId { get; set; } = "standard";

    /// <summary>Gets or sets default enabled folder ids.</summary>
    [JsonPropertyName("defaultEnabledFolderIds")]
    public List<string> DefaultEnabledFolderIds { get; set; } = [];

    /// <summary>Gets or sets policy presets.</summary>
    [JsonPropertyName("policyPresets")]
    public List<SignupPolicyPresetDto> PolicyPresets { get; set; } = [];

    /// <summary>Gets or sets login page signup button settings.</summary>
    [JsonPropertyName("loginButtonSettings")]
    public SignupLoginButtonSettingsDto LoginButtonSettings { get; set; } = new();

    /// <summary>Gets or sets email settings.</summary>
    [JsonPropertyName("emailSettings")]
    public SignupEmailSettingsDto EmailSettings { get; set; } = new();

    /// <summary>Gets or sets public signup page appearance settings.</summary>
    [JsonPropertyName("appearanceSettings")]
    public SignupAppearanceSettingsDto AppearanceSettings { get; set; } = new();

    /// <summary>Gets or sets native signup users.</summary>
    [JsonPropertyName("users")]
    public List<SignupUserDto> Users { get; set; } = [];

    /// <summary>Gets or sets pending reset code count.</summary>
    [JsonPropertyName("pendingResetCount")]
    public int PendingResetCount { get; set; }

    /// <summary>Gets or sets pending email verification count.</summary>
    [JsonPropertyName("pendingVerificationCount")]
    public int PendingVerificationCount { get; set; }
}

/// <summary>
/// Native signup admin settings patch.
/// </summary>
public sealed class SignupAdminSettingsPatch
{
    /// <summary>Gets or sets whether native signup is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Gets or sets whether a valid invite code is required.</summary>
    [JsonPropertyName("requireInvite")]
    public bool? RequireInvite { get; set; }

    /// <summary>Gets or sets default policy preset id.</summary>
    [JsonPropertyName("defaultPolicyPresetId")]
    public string? DefaultPolicyPresetId { get; set; }

    /// <summary>Gets or sets default enabled folder ids.</summary>
    [JsonPropertyName("defaultEnabledFolderIds")]
    public List<string>? DefaultEnabledFolderIds { get; set; }

    /// <summary>Gets or sets email settings.</summary>
    [JsonPropertyName("emailSettings")]
    public SignupEmailSettingsDto? EmailSettings { get; set; }

    /// <summary>Gets or sets login page signup button settings.</summary>
    [JsonPropertyName("loginButtonSettings")]
    public SignupLoginButtonSettingsDto? LoginButtonSettings { get; set; }

    /// <summary>Gets or sets public signup page appearance settings.</summary>
    [JsonPropertyName("appearanceSettings")]
    public SignupAppearanceSettingsDto? AppearanceSettings { get; set; }

    /// <summary>Gets or sets optional Jellyfin user email mappings for password reset.</summary>
    [JsonPropertyName("users")]
    public List<SignupUserEmailPatch>? Users { get; set; }
}

/// <summary>
/// Test signup email request.
/// </summary>
public sealed class SignupTestEmailRequest
{
    /// <summary>Gets or sets recipient email address.</summary>
    [JsonPropertyName("recipient")]
    public string? Recipient { get; set; }
}

