using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Signup.Configuration;
using Jellyfin.Plugin.Signup.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Signup.Services;

/// <summary>
/// Native Jellyfin signup service backed by Jellyfin local users.
/// </summary>
public sealed class SignupService : ISignupService
{
    private const string ExpiredVerificationMessage = "This verification link has expired. Please sign up again.";
    private const string ResetSuccessMessage = "If that email belongs to a Jellyfin account, a reset code has been sent.";
    private const string TooManyAttemptsMessage = "Too many attempts. Please wait a few minutes and try again.";
    private const string DefaultAuthenticationProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";
    private const string DefaultPasswordResetProviderId = "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider";
    private static readonly Regex UsernameRegex = new("^[A-Za-z0-9._-]{3,32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailRegex = new("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<SignupService> _logger;
    private readonly IDataProtector _passwordProtector;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignupService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="dataProtectionProvider">Data protection provider.</param>
    public SignupService(
        ILogger<SignupService> logger,
        IUserManager userManager,
        IDataProtectionProvider dataProtectionProvider)
    {
        _logger = logger;
        _passwordProtector = dataProtectionProvider.CreateProtector("Jellyfin.Signup.PendingPassword.v1");
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<SignupInviteValidationResponse> ValidateInviteAsync(
        string? inviteCode,
        string remoteAddress,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var configuration = GetConfiguration();

            if (string.IsNullOrWhiteSpace(inviteCode))
            {
                var openSignup = configuration.SignupSettings.Enabled && !configuration.SignupSettings.RequireInvite;

                return new SignupInviteValidationResponse
                {
                    EmailRequired = true,
                    Message = openSignup
                        ? "Invite codes are no longer required. Email is required for signup."
                        : configuration.SignupSettings.Enabled
                            ? "A valid invite code is required."
                            : "Jellyfin signup is not enabled.",
                    Ok = openSignup,
                    PolicyPresetName = FindPreset(configuration.SignupSettings, configuration.SignupSettings.DefaultPolicyPresetId).Name,
                    Valid = openSignup
                };
            }

            var invite = FindInvite(configuration.SignupSettings, inviteCode);
            var validation = ValidateInvite(configuration.SignupSettings, invite);

            if (!validation.Valid)
            {
                RegisterFailedAttempt(configuration, invite, remoteAddress);
                SaveConfiguration(configuration);
                return validation;
            }

            invite!.FailedAttempts = 0;
            invite.LockedUntilUtc = null;
            SaveConfiguration(configuration);

            return validation;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SignupResponse> CreateUserAsync(
        SignupCreateUserRequest request,
        string remoteAddress,
        CancellationToken cancellationToken)
    {
        SignupEmailSettings emailSettings;
        SignupInvite? invite;
        SignupPolicyPreset preset;
        SignupPendingVerification pending;
        List<string> enabledFolderIds;
        string verificationToken;
        string verificationLink;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var configuration = GetConfiguration();
            var signup = configuration.SignupSettings;
            CleanupSignupState(signup);

            if (!signup.Enabled)
            {
                return Fail("Jellyfin signup is not enabled.");
            }

            if (!IsEmailConfigured(signup.EmailSettings))
            {
                return Fail("Email verification is not configured yet.");
            }

            var username = CleanUsername(request.Username);
            var email = CleanEmail(request.Email);
            var passwordError = ValidatePassword(request.Password, request.ConfirmPassword);
            var verificationBaseUrl = ResolveVerificationBaseUrl(signup.EmailSettings.PublicSignupUrl, request.VerificationBaseUrl);

            if (IsRateLimited(signup, "signup-create", remoteAddress, email ?? username, 8, 15, 15))
            {
                SaveConfiguration(configuration);
                return Fail(TooManyAttemptsMessage);
            }

            if (username is null)
            {
                return Fail("Use 3-32 letters, numbers, dots, dashes, or underscores for the username.");
            }

            if (email is null)
            {
                return Fail("A valid email address is required.");
            }

            if (passwordError is not null)
            {
                return Fail(passwordError);
            }

            if (_userManager.GetUserByName(username) is not null)
            {
                return Fail("That username is already taken.");
            }

            if (EmailExists(signup, email))
            {
                return Fail("That email is already registered.");
            }

            if (PendingSignupExists(signup, username, email))
            {
                signup.PendingVerifications.RemoveAll(candidate =>
                    string.Equals(candidate.Username, username, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));
            }

            invite = null;
            if (!string.IsNullOrWhiteSpace(request.InviteCode))
            {
                invite = FindInvite(signup, request.InviteCode);
                var validation = ValidateInvite(signup, invite);

                if (!validation.Valid)
                {
                    RegisterFailedAttempt(configuration, invite, remoteAddress);
                    SaveConfiguration(configuration);
                    return Fail(validation.Message);
                }

                invite!.FailedAttempts = 0;
                invite.LockedUntilUtc = null;
            }
            else if (signup.RequireInvite)
            {
                return Fail("A valid invite code is required.");
            }

            preset = FindPreset(signup, invite?.PolicyPresetId ?? signup.DefaultPolicyPresetId);
            enabledFolderIds = invite?.EnabledFolderIds.Count > 0
                ? invite.EnabledFolderIds.ToList()
                : signup.DefaultEnabledFolderIds.ToList();
            verificationToken = CreateVerificationToken();
            pending = new SignupPendingVerification
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Email = email,
                EnabledFolderIds = enabledFolderIds,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(signup.EmailSettings.VerificationLinkMinutes),
                Id = Guid.NewGuid().ToString("N"),
                InviteId = invite?.Id ?? string.Empty,
                ProtectedPassword = _passwordProtector.Protect(request.Password!),
                PolicyPresetId = preset.Id,
                RemoteAddress = remoteAddress,
                TokenHash = HashVerificationToken(verificationToken),
                Username = username
            };
            signup.PendingVerifications.Add(pending);

            emailSettings = CloneEmailSettings(signup.EmailSettings);
            SaveConfiguration(configuration);
            verificationLink = BuildVerificationLink(verificationBaseUrl, verificationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jellyfin signup failed. Remote={RemoteAddress}.", remoteAddress);
            return Fail("Signup failed. Please check the details and try again.");
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await SendVerificationEmailAsync(emailSettings, pending, verificationLink, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Jellyfin signup pending email verification. Username={Username}; Email={Email}; PendingId={PendingId}; Remote={RemoteAddress}.",
                pending.Username,
                RedactEmail(pending.Email),
                pending.Id,
                remoteAddress);

            return new SignupResponse
            {
                Ok = true,
                Message = "Check your email to verify your address and finish creating your Jellyfin account.",
                Username = pending.Username
            };
        }
        catch (Exception ex)
        {
            await RemovePendingVerificationAsync(pending.Id, cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Jellyfin signup verification email failed. Email={Email}.", RedactEmail(pending.Email));
            return Fail("Verification email could not be sent. Please try again later.");
        }
    }

    /// <inheritdoc />
    public async Task<SignupResponse> VerifyEmailAsync(
        SignupEmailVerificationRequest request,
        string remoteAddress,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var token = CleanVerificationToken(request.Token);

            if (token is null)
            {
                return Fail("Verification link is invalid.");
            }

            var configuration = GetConfiguration();
            var signup = configuration.SignupSettings;
            CleanupPendingVerifications(signup);

            var tokenHash = HashVerificationToken(token);
            if (IsRateLimited(signup, "verify-email", remoteAddress, tokenHash, 20, 15, 15))
            {
                SaveConfiguration(configuration);
                return Fail(TooManyAttemptsMessage);
            }

            var pending = signup.PendingVerifications.FirstOrDefault(candidate =>
                string.Equals(candidate.TokenHash, tokenHash, StringComparison.OrdinalIgnoreCase));

            if (pending is null)
            {
                SaveConfiguration(configuration);
                return Fail(ExpiredVerificationMessage);
            }

            if (_userManager.GetUserByName(pending.Username) is not null)
            {
                signup.PendingVerifications.Remove(pending);
                SaveConfiguration(configuration);
                return Fail("That username is already taken.");
            }

            if (EmailExists(signup, pending.Email))
            {
                signup.PendingVerifications.Remove(pending);
                SaveConfiguration(configuration);
                return Fail("That email is already registered.");
            }

            string password;

            try
            {
                password = _passwordProtector.Unprotect(pending.ProtectedPassword);
            }
            catch (Exception ex)
            {
                signup.PendingVerifications.Remove(pending);
                SaveConfiguration(configuration);
                _logger.LogError(ex, "Jellyfin pending signup password could not be unprotected. PendingId={PendingId}.", pending.Id);
                return Fail("Verification could not be completed. Please sign up again.");
            }

            var invite = string.IsNullOrWhiteSpace(pending.InviteId)
                ? null
                : signup.Invites.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, pending.InviteId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(pending.InviteId))
            {
                var validation = ValidateInvite(signup, invite);

                if (!validation.Valid)
                {
                    signup.PendingVerifications.Remove(pending);
                    SaveConfiguration(configuration);
                    return Fail("The invite used for this signup is no longer valid. Please request a new invite.");
                }
            }

            var preset = FindPreset(signup, pending.PolicyPresetId);
            User createdUser;

            try
            {
                createdUser = await _userManager.CreateUserAsync(pending.Username).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                signup.PendingVerifications.Remove(pending);
                SaveConfiguration(configuration);
                _logger.LogWarning(
                    ex,
                    "Jellyfin email verification could not create user because the username is unavailable. Username={Username}; PendingId={PendingId}; Remote={RemoteAddress}.",
                    pending.Username,
                    pending.Id,
                    remoteAddress);
                return Fail("That username is already taken. Please sign up again with a different username.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Jellyfin email verification could not create user. Username={Username}; PendingId={PendingId}; Remote={RemoteAddress}.",
                    pending.Username,
                    pending.Id,
                    remoteAddress);
                return Fail("Jellyfin could not create this user. Please try the link again or sign up again.");
            }

            try
            {
                await _userManager.ChangePassword(createdUser.Id, password).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await DeleteCreatedUserAfterVerificationFailureAsync(createdUser.Id, pending.Id).ConfigureAwait(false);
                _logger.LogError(
                    ex,
                    "Jellyfin email verification could not set user password. Username={Username}; PendingId={PendingId}; Remote={RemoteAddress}.",
                    pending.Username,
                    pending.Id,
                    remoteAddress);
                return Fail("Jellyfin created the user but could not set the password. Please sign up again with a different password.");
            }

            try
            {
                await ApplyPolicyAsync(createdUser.Id, new UserPolicy(), preset, pending.EnabledFolderIds, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await DeleteCreatedUserAfterVerificationFailureAsync(createdUser.Id, pending.Id).ConfigureAwait(false);
                _logger.LogError(
                    ex,
                    "Jellyfin email verification could not apply signup policy. Username={Username}; PendingId={PendingId}; Preset={PresetId}; Remote={RemoteAddress}.",
                    pending.Username,
                    pending.Id,
                    preset.Id,
                    remoteAddress);
                return Fail("Jellyfin created the user but could not apply the signup policy. Check the invite policy or folder IDs, then sign up again.");
            }

            signup.PendingVerifications.Remove(pending);
            var userRecord = new SignupUserRecord
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Email = pending.Email,
                PolicyPresetId = preset.Id,
                RemoteAddress = remoteAddress,
                UserId = createdUser.Id.ToString("N"),
                Username = pending.Username
            };
            signup.Users.Add(userRecord);

            invite?.UsageRecords.Add(new SignupInviteUsageRecord
            {
                Email = userRecord.Email,
                RemoteAddress = remoteAddress,
                UsedAtUtc = userRecord.CreatedAtUtc,
                UserId = userRecord.UserId,
                Username = userRecord.Username
            });

            SaveConfiguration(configuration);

            _logger.LogInformation(
                "Jellyfin email verified and Jellyfin user created. Username={Username}; Email={Email}; PendingId={PendingId}; Remote={RemoteAddress}.",
                pending.Username,
                RedactEmail(pending.Email),
                pending.Id,
                remoteAddress);

            return new SignupResponse
            {
                Ok = true,
                Message = "Email verified. Your Jellyfin account is ready and you can sign in now.",
                UserId = createdUser.Id.ToString("N"),
                Username = pending.Username
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jellyfin email verification failed. Remote={RemoteAddress}.", remoteAddress);
            return Fail("Email verification failed. Please try the link again or sign up again.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SignupResponse> RequestPasswordResetAsync(
        SignupPasswordResetRequest request,
        string remoteAddress,
        CancellationToken cancellationToken)
    {
        SignupEmailSettings emailSettings;
        SignupUserRecord? userRecord;
        string resetCode;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var configuration = GetConfiguration();
            var signup = configuration.SignupSettings;
            var email = CleanEmail(request.Email);
            CleanupSignupState(signup);

            if (email is null)
            {
                return Fail("Enter a valid email address.");
            }

            if (IsRateLimited(signup, "password-reset-request", remoteAddress, email, 6, 15, 30))
            {
                SaveConfiguration(configuration);
                return new SignupResponse { Ok = true, Message = ResetSuccessMessage };
            }

            if (!IsEmailConfigured(signup.EmailSettings))
            {
                return Fail("Password reset email is not configured yet.");
            }

            userRecord = FindUserByEmail(signup, email);

            if (userRecord is null)
            {
                _logger.LogWarning("Jellyfin password reset requested for unknown email. Email={Email}; Remote={RemoteAddress}.", RedactEmail(email), remoteAddress);
                SaveConfiguration(configuration);
                return new SignupResponse { Ok = true, Message = ResetSuccessMessage };
            }

            resetCode = CreateResetCode();
            signup.PasswordResetCodes.Add(new SignupPasswordResetCode
            {
                CodeHash = HashResetCode(email, resetCode),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Email = email,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(signup.EmailSettings.ResetCodeMinutes),
                RemoteAddress = remoteAddress,
                Username = userRecord.Username
            });

            emailSettings = CloneEmailSettings(signup.EmailSettings);
            SaveConfiguration(configuration);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await SendResetEmailAsync(emailSettings, userRecord!, resetCode, cancellationToken).ConfigureAwait(false);
            return new SignupResponse { Ok = true, Message = ResetSuccessMessage };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jellyfin password reset email failed. Email={Email}.", RedactEmail(userRecord!.Email));
            return Fail("Password reset email could not be sent. Please try again later.");
        }
    }

    /// <inheritdoc />
    public async Task<SignupResponse> ConfirmPasswordResetAsync(
        SignupPasswordResetConfirmRequest request,
        string remoteAddress,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var configuration = GetConfiguration();
            var signup = configuration.SignupSettings;
            var email = CleanEmail(request.Email);
            var code = CleanResetCode(request.Code);
            var passwordError = ValidatePassword(request.Password, request.ConfirmPassword);
            CleanupSignupState(signup);

            if (email is null)
            {
                return Fail("Enter a valid email address.");
            }

            if (code is null)
            {
                return Fail("Enter the reset code from your email.");
            }

            if (passwordError is not null)
            {
                return Fail(passwordError);
            }

            if (IsRateLimited(signup, "password-reset-confirm", remoteAddress, email, 10, 15, 30))
            {
                SaveConfiguration(configuration);
                return Fail(TooManyAttemptsMessage);
            }

            var codeHash = HashResetCode(email, code);
            var reset = signup.PasswordResetCodes
                .Where(candidate => candidate.UsedAtUtc is null)
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.CodeHash, codeHash, StringComparison.OrdinalIgnoreCase));

            if (reset is null || reset.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                RegisterResetFailure(signup, email, remoteAddress);
                SaveConfiguration(configuration);
                return Fail("Reset code is invalid or expired.");
            }

            if (reset.FailedAttempts >= signup.FailedAttemptLimit)
            {
                return Fail("Too many reset attempts. Request a new reset code.");
            }

            var userRecord = FindUserByEmail(signup, email);
            var user = userRecord is null ? _userManager.GetUserByName(reset.Username) : FindJellyfinUser(userRecord);

            if (user is null)
            {
                reset.FailedAttempts++;
                SaveConfiguration(configuration);
                return Fail("The account for this reset code was not found.");
            }

            await _userManager.ChangePassword(user.Id, request.Password!).ConfigureAwait(false);
            signup.PasswordResetCodes.Remove(reset);
            SaveConfiguration(configuration);

            _logger.LogInformation(
                "Jellyfin password reset completed. Username={Username}; Email={Email}; Remote={RemoteAddress}.",
                reset.Username,
                RedactEmail(email),
                remoteAddress);

            return new SignupResponse { Ok = true, Message = "Password reset. You can sign in with the new password." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jellyfin password reset confirmation failed. Remote={RemoteAddress}.", remoteAddress);
            return Fail("Password reset failed. Please request a new code and try again.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public SignupLoginButtonPublicResponse GetPublicLoginButton()
    {
        _gate.Wait();

        try
        {
            var signup = GetConfiguration().SignupSettings;
            var button = signup.LoginButtonSettings;

            return new SignupLoginButtonPublicResponse
            {
                Enabled = signup.Enabled && button.Enabled,
                TargetUrl = string.IsNullOrWhiteSpace(button.TargetUrl) ? "signup.html" : button.TargetUrl,
                Text = button.Text
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public SignupPublicSettingsResponse GetPublicSettings()
    {
        _gate.Wait();

        try
        {
            var signup = GetConfiguration().SignupSettings;

            return new SignupPublicSettingsResponse
            {
                AppearanceSettings = SignupAppearanceSettingsDto.FromSettings(signup.AppearanceSettings),
                EmailEnabled = IsEmailConfigured(signup.EmailSettings),
                Enabled = signup.Enabled,
                RequireInvite = signup.RequireInvite
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public SignupAdminSettingsResponse GetAdminSettings()
    {
        _gate.Wait();

        try
        {
            var configuration = GetConfiguration();
            if (CleanupSignupState(configuration.SignupSettings) > 0)
            {
                SaveConfiguration(configuration);
            }

            return CreateAdminSettingsResponse(configuration.SignupSettings);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public SignupAdminSettingsResponse SaveAdminSettings(SignupAdminSettingsPatch request)
    {
        _gate.Wait();

        try
        {
            var configuration = GetConfiguration();
            var signup = configuration.SignupSettings;
            CleanupSignupState(signup);

            if (request.Enabled is not null)
            {
                signup.Enabled = request.Enabled.Value;
            }

            if (request.RequireInvite is not null)
            {
                signup.RequireInvite = request.RequireInvite.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.DefaultPolicyPresetId))
            {
                signup.DefaultPolicyPresetId = CleanIdentifier(request.DefaultPolicyPresetId, "standard");
            }

            if (request.DefaultEnabledFolderIds is not null)
            {
                signup.DefaultEnabledFolderIds = CleanStringList(request.DefaultEnabledFolderIds, 120);
            }

            if (request.EmailSettings is not null)
            {
                ApplyEmailSettingsPatch(signup.EmailSettings, request.EmailSettings);
            }

            if (request.LoginButtonSettings is not null)
            {
                ApplyLoginButtonSettingsPatch(signup.LoginButtonSettings, request.LoginButtonSettings);
            }

            if (request.AppearanceSettings is not null)
            {
                ApplyAppearanceSettingsPatch(signup.AppearanceSettings, request.AppearanceSettings);
            }

            if (request.Users is not null)
            {
                ApplyUserEmailMappings(signup, request.Users);
            }

            SaveConfiguration(configuration);

            return CreateAdminSettingsResponse(configuration.SignupSettings);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SignupResponse> SendTestEmailAsync(SignupTestEmailRequest request, CancellationToken cancellationToken)
    {
        SignupEmailSettings settings;
        string recipient;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var signup = GetConfiguration().SignupSettings;

            if (!IsEmailConfigured(signup.EmailSettings))
            {
                return Fail("Email is not configured yet.");
            }

            if (!string.IsNullOrWhiteSpace(request.Recipient) && CleanEmail(request.Recipient) is null)
            {
                return Fail("Enter a valid test recipient email address.");
            }

            recipient = CleanEmail(request.Recipient) ?? signup.EmailSettings.FromAddress;
            settings = CloneEmailSettings(signup.EmailSettings);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await SendEmailAsync(
                settings,
                recipient,
                "Jellyfin test email",
                "Jellyfin email delivery is configured and working.",
                cancellationToken).ConfigureAwait(false);

            return new SignupResponse { Ok = true, Message = "Test email sent." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jellyfin test email failed. Recipient={Recipient}.", RedactEmail(recipient));
            return Fail($"SMTP test failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public SignupInviteListResponse GetAdminInvites()
    {
        var configuration = GetConfiguration();
        var signup = configuration.SignupSettings;

        return new SignupInviteListResponse
        {
            Enabled = signup.Enabled,
            RequireInvite = signup.RequireInvite,
            Invites = signup.Invites
                .OrderByDescending(invite => invite.CreatedAtUtc)
                .Select(invite => SignupInviteDto.FromInvite(invite))
                .ToList(),
            PolicyPresets = signup.PolicyPresets
                .Select(SignupPolicyPresetDto.FromPreset)
                .ToList()
        };
    }

    /// <inheritdoc />
    public SignupInviteDto CreateInvite(SignupCreateInviteRequest request, string createdBy)
    {
        _gate.Wait();

        try
        {
            var configuration = GetConfiguration();
            var signup = configuration.SignupSettings;
            var code = CreateInviteCode();
            var label = CleanText(request.Label, "Jellyfin Invite", 80);
            var presetId = CleanIdentifier(request.PolicyPresetId, "standard");

            if (!signup.PolicyPresets.Any(preset => string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase)))
            {
                presetId = "standard";
            }

            var invite = new SignupInvite
            {
                CodeHash = HashInviteCode(code),
                CodePreview = code.Length <= 6 ? code : code[^6..],
                CreatedAtUtc = DateTimeOffset.UtcNow,
                EmailRequired = request.EmailRequired ?? true,
                EnabledFolderIds = CleanStringList(request.EnabledFolderIds, 120),
                ExpiresAtUtc = request.ExpiresAtUtc,
                Id = CleanIdentifier($"{label}-{Guid.NewGuid():N}", "invite"),
                Label = label,
                MaxUses = Math.Max(0, request.MaxUses ?? 1),
                PolicyPresetId = presetId
            };

            signup.Invites.Add(invite);
            SaveConfiguration(configuration);

            _logger.LogInformation(
                "Jellyfin signup invite created. InviteId={InviteId}; Label={Label}; CreatedBy={CreatedBy}; Preset={PresetId}.",
                invite.Id,
                invite.Label,
                createdBy,
                invite.PolicyPresetId);

            return SignupInviteDto.FromInvite(invite, code);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public SignupInviteDto DisableInvite(string inviteId)
    {
        _gate.Wait();

        try
        {
            var configuration = GetConfiguration();
            var invite = GetInviteById(configuration.SignupSettings, inviteId);

            invite.Disabled = true;
            SaveConfiguration(configuration);

            return SignupInviteDto.FromInvite(invite);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void DeleteInvite(string inviteId)
    {
        _gate.Wait();

        try
        {
            var configuration = GetConfiguration();
            var invite = GetInviteById(configuration.SignupSettings, inviteId);

            configuration.SignupSettings.Invites.Remove(invite);
            SaveConfiguration(configuration);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyPolicyAsync(
        Guid userId,
        UserPolicy policy,
        SignupPolicyPreset preset,
        IReadOnlyCollection<string> folderOverride,
        CancellationToken cancellationToken)
    {
        policy.IsAdministrator = false;
        policy.IsDisabled = false;
        policy.IsHidden = false;
        policy.AuthenticationProviderId = DefaultAuthenticationProviderId;
        policy.PasswordResetProviderId = DefaultPasswordResetProviderId;
        policy.LoginAttemptsBeforeLockout = -1;
        policy.InvalidLoginAttemptCount = 0;
        policy.EnableCollectionManagement = false;
        policy.EnableSubtitleManagement = false;
        policy.EnableLyricManagement = false;
        policy.EnableUserPreferenceAccess = true;
        policy.EnableRemoteControlOfOtherUsers = preset.AllowRemoteControl;
        policy.EnableSharedDeviceControl = preset.AllowRemoteControl;
        policy.EnableLiveTvManagement = false;
        policy.EnableLiveTvAccess = preset.AllowLiveTv;
        policy.EnableMediaPlayback = true;
        policy.EnableContentDeletion = false;
        policy.EnableContentDeletionFromFolders = [];
        policy.EnableContentDownloading = preset.AllowDownloads;
        policy.EnableMediaConversion = false;
        policy.EnablePublicSharing = false;
        policy.EnableSyncTranscoding = preset.AllowTranscoding;
        policy.EnableAudioPlaybackTranscoding = preset.AllowTranscoding;
        policy.EnableVideoPlaybackTranscoding = preset.AllowTranscoding;
        policy.EnablePlaybackRemuxing = preset.AllowTranscoding;
        policy.ForceRemoteSourceTranscoding = false;
        policy.EnableAllDevices = true;
        policy.EnabledDevices = [];
        policy.EnableAllChannels = true;
        policy.EnabledChannels = [];
        policy.BlockedChannels = null;
        policy.BlockedMediaFolders = null;
        policy.RemoteClientBitrateLimit = preset.RemoteClientBitrateLimit;
        policy.MaxActiveSessions = preset.MaxActiveSessions;
        policy.MaxParentalRating = preset.MaxParentalRating;

        var effectiveFolderOverride = folderOverride.Count > 0 ? folderOverride : preset.EnabledFolderIds;
        var enabledFolderIds = CleanGuidArray(effectiveFolderOverride);

        policy.EnableAllFolders = preset.EnableAllFolders && effectiveFolderOverride.Count == 0;
        policy.EnabledFolders = policy.EnableAllFolders ? [] : enabledFolderIds;

        await _userManager.UpdatePolicyAsync(userId, policy).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private SignupInviteValidationResponse ValidateInvite(SignupSettings signup, SignupInvite? invite)
    {
        if (!signup.Enabled)
        {
            return Invalid("Jellyfin signup is not enabled.");
        }

        if (invite is null)
        {
            return Invalid("Invite code was not found.");
        }

        var now = DateTimeOffset.UtcNow;

        if (invite.Disabled)
        {
            return Invalid("This invite has been disabled.");
        }

        if (invite.LockedUntilUtc is not null && invite.LockedUntilUtc > now)
        {
            return Invalid("This invite is temporarily locked. Try again later.");
        }

        if (invite.ExpiresAtUtc is not null && invite.ExpiresAtUtc <= now)
        {
            return Invalid("This invite has expired.");
        }

        if (invite.MaxUses > 0 && invite.UsageRecords.Count >= invite.MaxUses)
        {
            return Invalid("This invite has already been used.");
        }

        var preset = FindPreset(signup, invite.PolicyPresetId);

        return new SignupInviteValidationResponse
        {
            EmailRequired = true,
            Label = invite.Label,
            Message = "Invite code accepted.",
            Ok = true,
            PolicyPresetName = preset.Name,
            Valid = true
        };
    }

    private SignupAdminSettingsResponse CreateAdminSettingsResponse(SignupSettings signup)
    {
        return new SignupAdminSettingsResponse
        {
            AppearanceSettings = SignupAppearanceSettingsDto.FromSettings(signup.AppearanceSettings),
            DefaultEnabledFolderIds = signup.DefaultEnabledFolderIds.ToList(),
            DefaultPolicyPresetId = signup.DefaultPolicyPresetId,
            EmailSettings = SignupEmailSettingsDto.FromSettings(signup.EmailSettings),
            Enabled = signup.Enabled,
            LoginButtonSettings = SignupLoginButtonSettingsDto.FromSettings(signup.LoginButtonSettings),
            RequireInvite = signup.RequireInvite,
            PendingVerificationCount = signup.PendingVerifications.Count(pending => pending.ExpiresAtUtc > DateTimeOffset.UtcNow),
            PendingResetCount = signup.PasswordResetCodes.Count(code => code.UsedAtUtc is null && code.ExpiresAtUtc > DateTimeOffset.UtcNow),
            PolicyPresets = signup.PolicyPresets.Select(SignupPolicyPresetDto.FromPreset).ToList(),
            Users = CreateUserEmailSettingsResponse(signup)
        };
    }

    private List<SignupUserDto> CreateUserEmailSettingsResponse(SignupSettings signup)
    {
        var recordsByUserId = signup.Users
            .Where(record => !string.IsNullOrWhiteSpace(record.UserId))
            .GroupBy(record => record.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var recordsByUsername = signup.Users
            .Where(record => !string.IsNullOrWhiteSpace(record.Username))
            .GroupBy(record => record.Username, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var seenUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var users = new List<SignupUserDto>();

        foreach (var jellyfinUser in _userManager.GetUsers().OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase))
        {
            var userId = jellyfinUser.Id.ToString("N");
            var record = recordsByUserId.GetValueOrDefault(userId)
                ?? recordsByUsername.GetValueOrDefault(jellyfinUser.Username);
            var email = record?.Email ?? string.Empty;

            seenUserIds.Add(userId);
            users.Add(new SignupUserDto
            {
                CreatedAtUtc = record?.CreatedAtUtc ?? DateTimeOffset.MinValue,
                Email = email,
                EmailConfigured = !string.IsNullOrWhiteSpace(email),
                PolicyPresetId = record?.PolicyPresetId ?? signup.DefaultPolicyPresetId,
                Source = record is null
                    ? "jellyfin"
                    : string.Equals(record.RemoteAddress, "admin-mapped", StringComparison.OrdinalIgnoreCase)
                        ? "admin"
                        : "signup",
                UserId = userId,
                Username = jellyfinUser.Username
            });
        }

        users.AddRange(signup.Users
            .Where(record => !string.IsNullOrWhiteSpace(record.UserId) && !seenUserIds.Contains(record.UserId))
            .OrderBy(record => record.Username, StringComparer.OrdinalIgnoreCase)
            .Select(record =>
            {
                var dto = SignupUserDto.FromRecord(record);
                dto.Source = "saved";
                return dto;
            }));

        return users.Take(1000).ToList();
    }

    private void ApplyUserEmailMappings(SignupSettings signup, IReadOnlyCollection<SignupUserEmailPatch> userMappings)
    {
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in userMappings.Where(mapping => mapping is not null))
        {
            var userId = CleanText(mapping.UserId, string.Empty, 64);
            if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var jellyfinUserId))
            {
                continue;
            }

            var jellyfinUser = _userManager.GetUserById(jellyfinUserId);
            if (jellyfinUser is null)
            {
                continue;
            }

            var requestedEmail = mapping.Email?.Trim();
            var email = CleanEmail(requestedEmail);
            var existing = signup.Users.FirstOrDefault(user =>
                string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(user.Username, jellyfinUser.Username, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(requestedEmail))
            {
                if (existing is not null)
                {
                    signup.Users.Remove(existing);
                }

                continue;
            }

            if (email is null)
            {
                continue;
            }

            if (!seenEmails.Add(email))
            {
                continue;
            }

            var duplicate = signup.Users.FirstOrDefault(user =>
                !ReferenceEquals(user, existing) &&
                !string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null)
            {
                continue;
            }

            if (existing is null)
            {
                signup.Users.Add(new SignupUserRecord
                {
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Email = email,
                    PolicyPresetId = signup.DefaultPolicyPresetId,
                    RemoteAddress = "admin-mapped",
                    UserId = userId,
                    Username = jellyfinUser.Username
                });
                continue;
            }

            existing.Email = email;
            existing.UserId = userId;
            existing.Username = jellyfinUser.Username;
        }
    }

    private void RegisterFailedAttempt(PluginConfiguration configuration, SignupInvite? invite, string remoteAddress)
    {
        if (invite is null)
        {
            _logger.LogWarning("Jellyfin signup failed with unknown invite. Remote={RemoteAddress}.", remoteAddress);
            return;
        }

        invite.FailedAttempts++;

        if (invite.FailedAttempts >= configuration.SignupSettings.FailedAttemptLimit)
        {
            invite.LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(configuration.SignupSettings.LockoutMinutes);
        }

        _logger.LogWarning(
            "Jellyfin signup invite validation failed. InviteId={InviteId}; FailedAttempts={FailedAttempts}; Remote={RemoteAddress}.",
            invite.Id,
            invite.FailedAttempts,
            remoteAddress);
    }

    private void RegisterResetFailure(SignupSettings signup, string email, string remoteAddress)
    {
        foreach (var reset in signup.PasswordResetCodes.Where(reset =>
            reset.UsedAtUtc is null && string.Equals(reset.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            reset.FailedAttempts++;
        }

        _logger.LogWarning(
            "Jellyfin password reset confirmation failed. Email={Email}; Remote={RemoteAddress}.",
            RedactEmail(email),
            remoteAddress);
    }

    private static bool IsRateLimited(
        SignupSettings signup,
        string action,
        string remoteAddress,
        string? secondaryKey,
        int maxAttempts,
        int windowMinutes,
        int lockoutMinutes)
    {
        var limitedByIp = RegisterRateLimitAttempt(
            signup,
            action,
            $"ip:{remoteAddress}",
            maxAttempts,
            windowMinutes,
            lockoutMinutes);

        if (string.IsNullOrWhiteSpace(secondaryKey))
        {
            return limitedByIp;
        }

        var limitedBySecondary = RegisterRateLimitAttempt(
            signup,
            action,
            $"value:{secondaryKey.Trim().ToLowerInvariant()}",
            maxAttempts,
            windowMinutes,
            lockoutMinutes);

        return limitedByIp || limitedBySecondary;
    }

    private static bool RegisterRateLimitAttempt(
        SignupSettings signup,
        string action,
        string key,
        int maxAttempts,
        int windowMinutes,
        int lockoutMinutes)
    {
        var now = DateTimeOffset.UtcNow;
        var actionId = CleanIdentifier(action, "signup");
        var keyHash = HashRateLimitKey($"{actionId}|{key}");
        var record = signup.RateLimits.FirstOrDefault(candidate =>
            string.Equals(candidate.Action, actionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.KeyHash, keyHash, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            signup.RateLimits.Add(new SignupRateLimitRecord
            {
                Action = actionId,
                Count = 1,
                KeyHash = keyHash,
                WindowStartedAtUtc = now
            });

            return false;
        }

        if (record.LockedUntilUtc is not null && record.LockedUntilUtc > now)
        {
            return true;
        }

        if (record.WindowStartedAtUtc.AddMinutes(windowMinutes) <= now)
        {
            record.Count = 1;
            record.LockedUntilUtc = null;
            record.WindowStartedAtUtc = now;
            return false;
        }

        record.Count++;

        if (record.Count <= maxAttempts)
        {
            return false;
        }

        record.LockedUntilUtc = now.AddMinutes(lockoutMinutes);
        return true;
    }

    private SignupInvite? FindInvite(SignupSettings signup, string? inviteCode)
    {
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            return null;
        }

        var hash = HashInviteCode(inviteCode);

        return signup.Invites.FirstOrDefault(invite =>
            string.Equals(invite.CodeHash, hash, StringComparison.OrdinalIgnoreCase));
    }

    private static SignupInvite GetInviteById(SignupSettings signup, string inviteId)
    {
        var invite = signup.Invites.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, inviteId, StringComparison.OrdinalIgnoreCase));

        return invite ?? throw new InvalidOperationException("Signup invite was not found.");
    }

    private static SignupPolicyPreset FindPreset(SignupSettings signup, string? presetId)
    {
        return signup.PolicyPresets.FirstOrDefault(preset =>
                string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase))
            ?? signup.PolicyPresets.FirstOrDefault(preset => preset.Id == "standard")
            ?? SignupSettings.CreateDefaultPolicyPresets()[0];
    }

    private static SignupUserRecord? FindUserByEmail(SignupSettings signup, string email)
    {
        return signup.Users.FirstOrDefault(record =>
            string.Equals(record.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private User? FindJellyfinUser(SignupUserRecord record)
    {
        if (Guid.TryParse(record.UserId, out var userId))
        {
            var user = _userManager.GetUserById(userId);

            if (user is not null)
            {
                return user;
            }
        }

        return _userManager.GetUserByName(record.Username);
    }

    private static bool EmailExists(SignupSettings signup, string email)
    {
        return signup.Users.Any(record => string.Equals(record.Email, email, StringComparison.OrdinalIgnoreCase))
            || signup.Invites
                .SelectMany(invite => invite.UsageRecords)
                .Any(record => string.Equals(record.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PendingSignupExists(SignupSettings signup, string username, string email)
    {
        return signup.PendingVerifications.Any(record =>
            string.Equals(record.Username, username, StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private static PluginConfiguration GetConfiguration()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        SignupSettingsNormalizer.Normalize(configuration);

        return configuration;
    }

    private static void SaveConfiguration(PluginConfiguration configuration)
    {
        if (Plugin.Instance is null)
        {
            throw new InvalidOperationException("Jellyfin plugin instance is unavailable.");
        }

        SignupSettingsNormalizer.Normalize(configuration);
        Plugin.Instance.UpdateConfiguration(configuration);
    }

    private static void ApplyLoginButtonSettingsPatch(SignupLoginButtonSettings settings, SignupLoginButtonSettingsDto patch)
    {
        settings.Enabled = patch.Enabled;
        settings.Text = CleanText(patch.Text, "Create Account", 40);
        settings.TargetUrl = CleanLoginButtonUrl(patch.TargetUrl);
    }

    private static void ApplyEmailSettingsPatch(SignupEmailSettings settings, SignupEmailSettingsDto patch)
    {
        settings.Enabled = patch.Enabled;
        settings.FromAddress = CleanEmail(patch.FromAddress) ?? string.Empty;
        settings.FromName = CleanText(patch.FromName, "Jellyfin", 80);
        settings.LogoUrl = CleanEmailLogoUrl(patch.LogoUrl);
        settings.PublicSignupUrl = CleanPublicSignupUrl(patch.PublicSignupUrl);
        settings.ResetCodeMinutes = Math.Clamp(patch.ResetCodeMinutes, 5, 120);
        settings.ResetSubject = CleanText(patch.ResetSubject, "Your Jellyfin password reset code", 120);
        settings.SmtpHost = CleanText(patch.SmtpHost, string.Empty, 160);
        settings.SmtpPort = Math.Clamp(patch.SmtpPort, 1, 65535);
        settings.UseSsl = patch.UseSsl;
        settings.VerificationLinkMinutes = Math.Clamp(patch.VerificationLinkMinutes, 10, 1440);
        settings.VerificationSubject = CleanText(patch.VerificationSubject, "Verify your Jellyfin email", 120);
        settings.Username = string.IsNullOrWhiteSpace(patch.Username) ? null : patch.Username.Trim();

        if (!string.IsNullOrWhiteSpace(patch.Password))
        {
            settings.Password = patch.Password.Trim();
        }
    }

    private static void ApplyAppearanceSettingsPatch(SignupAppearanceSettings settings, SignupAppearanceSettingsDto patch)
    {
        settings.BackgroundColor = CleanHexColor(patch.BackgroundColor, "#08070d");
        settings.Heading = CleanText(patch.Heading, "Jellyfin Signup", 80);
        settings.IntroText = CleanText(patch.IntroText, "Create your Jellyfin account, then verify your email before signing in.", 180);
        settings.LogoUrl = CleanPublicAssetUrl(patch.LogoUrl, string.Empty);
        settings.MutedTextColor = CleanHexColor(patch.MutedTextColor, "#bbb6c8");
        settings.PageTitle = CleanText(patch.PageTitle, "Jellyfin Signup", 80);
        settings.PanelColor = CleanHexColor(patch.PanelColor, "#15131d");
        settings.PrimaryColor = CleanHexColor(patch.PrimaryColor, "#00a4dc");
        settings.SecondaryColor = CleanHexColor(patch.SecondaryColor, "#aa5cc3");
        settings.TextColor = CleanHexColor(patch.TextColor, "#f7f7fb");
    }

    private static bool IsEmailConfigured(SignupEmailSettings settings)
    {
        return settings.Enabled
            && !string.IsNullOrWhiteSpace(settings.SmtpHost)
            && settings.SmtpPort > 0
            && !string.IsNullOrWhiteSpace(settings.FromAddress);
    }

    private static SignupEmailSettings CloneEmailSettings(SignupEmailSettings source) => new()
    {
        Enabled = source.Enabled,
        FromAddress = source.FromAddress,
        FromName = source.FromName,
        LogoUrl = source.LogoUrl,
        Password = source.Password,
        PublicSignupUrl = source.PublicSignupUrl,
        ResetCodeMinutes = source.ResetCodeMinutes,
        ResetSubject = source.ResetSubject,
        SmtpHost = source.SmtpHost,
        SmtpPort = source.SmtpPort,
        UseSsl = source.UseSsl,
        Username = source.Username,
        VerificationLinkMinutes = source.VerificationLinkMinutes,
        VerificationSubject = source.VerificationSubject
    };

    private static async Task SendVerificationEmailAsync(
        SignupEmailSettings settings,
        SignupPendingVerification pending,
        string verificationLink,
        CancellationToken cancellationToken)
    {
        var plainText = new StringBuilder()
            .AppendLine($"Hi {pending.Username},")
            .AppendLine()
            .AppendLine("Welcome to Jellyfin. Please verify your email to finish creating your account.")
            .AppendLine()
            .AppendLine(verificationLink)
            .AppendLine()
            .AppendLine($"This verification link expires after {settings.VerificationLinkMinutes} minutes.")
            .AppendLine("If you did not request this account, you can ignore this email.")
            .ToString();
        var html = BuildVerificationEmailHtml(settings, pending, verificationLink);

        await SendEmailAsync(settings, pending.Email, settings.VerificationSubject, plainText, cancellationToken, html).ConfigureAwait(false);
    }

    private static async Task SendResetEmailAsync(
        SignupEmailSettings settings,
        SignupUserRecord userRecord,
        string resetCode,
        CancellationToken cancellationToken)
    {
        var body = new StringBuilder()
            .AppendLine($"Hi {userRecord.Username},")
            .AppendLine()
            .AppendLine("Use this Jellyfin password reset code:")
            .AppendLine()
            .AppendLine(resetCode)
            .AppendLine()
            .AppendLine($"This code expires in {settings.ResetCodeMinutes} minutes.")
            .AppendLine("If you did not request this, you can ignore this email.")
            .ToString();

        await SendEmailAsync(settings, userRecord.Email, settings.ResetSubject, body, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildVerificationEmailHtml(
        SignupEmailSettings settings,
        SignupPendingVerification pending,
        string verificationLink)
    {
        var logoUrl = ResolveEmailLogoUrl(settings.LogoUrl, verificationLink);
        var safeLogoUrl = WebUtility.HtmlEncode(logoUrl);
        var safeLink = WebUtility.HtmlEncode(verificationLink);
        var safeUsername = WebUtility.HtmlEncode(pending.Username);
        var expiryText = $"This verification link expires after {settings.VerificationLinkMinutes} minutes.";

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Verify your Jellyfin email</title>
</head>
<body style="margin:0;background:#08050f;color:#f7f4ff;font-family:Arial,Helvetica,sans-serif;">
  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#08050f;padding:32px 12px;">
    <tr>
      <td align="center">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:linear-gradient(145deg,#151126,#231036);border:1px solid #3a2a66;border-radius:22px;box-shadow:0 20px 70px rgba(36,16,76,.45);overflow:hidden;">
          <tr>
            <td align="center" style="padding:34px 28px 10px;">
              <img src="{{safeLogoUrl}}" alt="Jellyfin" width="220" style="display:block;max-width:220px;width:70%;height:auto;border:0;">
            </td>
          </tr>
          <tr>
            <td style="padding:16px 34px 4px;text-align:center;">
              <h1 style="margin:0 0 12px;font-size:30px;line-height:1.2;color:#ffffff;">Welcome, {{safeUsername}}</h1>
              <p style="margin:0;color:#ddd7f2;font-size:16px;line-height:1.6;">Welcome to Jellyfin. Please verify your email to finish creating your account.</p>
            </td>
          </tr>
          <tr>
            <td align="center" style="padding:30px 34px 34px;">
              <a href="{{safeLink}}" style="display:inline-block;background:#20aee5;color:#05040a;text-decoration:none;font-weight:700;font-size:16px;line-height:1;padding:16px 30px;border-radius:999px;box-shadow:0 0 28px rgba(32,174,229,.42);">Verify Email</a>
            </td>
          </tr>
          <tr>
            <td style="padding:0 34px 30px;text-align:center;">
              <p style="margin:0;color:#bcb4d7;font-size:13px;line-height:1.5;">{{WebUtility.HtmlEncode(expiryText)}}</p>
              <p style="margin:12px 0 0;color:#827996;font-size:12px;line-height:1.5;">If you did not request this account, you can safely ignore this email.</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    private static async Task SendEmailAsync(
        SignupEmailSettings settings,
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken,
        string? htmlBody = null)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(recipient);

        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(body, Encoding.UTF8, MediaTypeNames.Text.Plain));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        using var smtp = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.UseSsl
        };

        if (!string.IsNullOrWhiteSpace(settings.Username) || !string.IsNullOrWhiteSpace(settings.Password))
        {
            smtp.Credentials = new NetworkCredential(settings.Username ?? string.Empty, settings.Password ?? string.Empty);
        }

        await smtp.SendMailAsync(message).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public int CleanupExpiredRecords()
    {
        _gate.Wait();

        try
        {
            var configuration = GetConfiguration();
            var removed = CleanupSignupState(configuration.SignupSettings);

            if (removed > 0)
            {
                SaveConfiguration(configuration);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int CleanupSignupState(SignupSettings signup)
    {
        var before = signup.PendingVerifications.Count + signup.PasswordResetCodes.Count + signup.RateLimits.Count;
        CleanupPendingVerifications(signup);
        CleanupResetCodes(signup);
        CleanupRateLimits(signup);

        return before - (signup.PendingVerifications.Count + signup.PasswordResetCodes.Count + signup.RateLimits.Count);
    }

    private static void CleanupResetCodes(SignupSettings signup)
    {
        var now = DateTimeOffset.UtcNow;
        signup.PasswordResetCodes = signup.PasswordResetCodes
            .Where(code => code.UsedAtUtc is null && code.ExpiresAtUtc > now)
            .TakeLast(250)
            .ToList();
    }

    private static void CleanupPendingVerifications(SignupSettings signup)
    {
        var now = DateTimeOffset.UtcNow;
        signup.PendingVerifications = signup.PendingVerifications
            .Where(pending => pending.ExpiresAtUtc > now)
            .TakeLast(500)
            .ToList();
    }

    private static void CleanupRateLimits(SignupSettings signup)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-6);

        signup.RateLimits = signup.RateLimits
            .Where(record => (record.LockedUntilUtc is not null && record.LockedUntilUtc > now)
                || record.WindowStartedAtUtc > cutoff)
            .TakeLast(1000)
            .ToList();
    }

    private async Task RemovePendingVerificationAsync(string pendingId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var configuration = GetConfiguration();
            configuration.SignupSettings.PendingVerifications.RemoveAll(pending =>
                string.Equals(pending.Id, pendingId, StringComparison.OrdinalIgnoreCase));
            SaveConfiguration(configuration);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DeleteCreatedUserAfterVerificationFailureAsync(Guid userId, string pendingId)
    {
        try
        {
            await _userManager.DeleteUserAsync(userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Jellyfin signup could not delete partially created user after verification failure. UserId={UserId}; PendingId={PendingId}.",
                userId,
                pendingId);
        }
    }

    private static string CreateInviteCode()
    {
        Span<byte> bytes = stackalloc byte[18];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    private static string CreateResetCode()
    {
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
    }

    private static string CreateVerificationToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    private static string HashInviteCode(string inviteCode)
    {
        var normalized = inviteCode.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashResetCode(string email, string code)
    {
        var normalized = $"{email.Trim().ToLowerInvariant()}|{code.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashVerificationToken(string token)
    {
        var normalized = token.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashRateLimitKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildVerificationLink(string baseUrl, string token)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        return $"{baseUrl}{separator}verify={Uri.EscapeDataString(token)}";
    }

    private static string ResolveEmailLogoUrl(string? logoUrl, string verificationLink)
    {
        var cleanedLogo = CleanEmailLogoUrl(logoUrl);

        if (Uri.TryCreate(cleanedLogo, UriKind.Absolute, out var absoluteLogo)
            && (absoluteLogo.Scheme == Uri.UriSchemeHttps || absoluteLogo.Scheme == Uri.UriSchemeHttp))
        {
            return absoluteLogo.ToString();
        }

        if (Uri.TryCreate(verificationLink, UriKind.Absolute, out var verificationUri))
        {
            return new Uri(verificationUri, cleanedLogo).ToString();
        }

        return cleanedLogo;
    }

    private static string? CleanUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();

        return UsernameRegex.IsMatch(cleaned) ? cleaned : null;
    }

    private static string? CleanEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();

        return EmailRegex.IsMatch(cleaned) ? cleaned.ToLowerInvariant() : null;
    }

    private static string? CleanResetCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Trim().Where(char.IsLetterOrDigit).ToArray());

        return cleaned.Length is >= 6 and <= 16 ? cleaned : null;
    }

    private static string? CleanVerificationToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();

        return cleaned.Length is >= 24 and <= 256 ? cleaned : null;
    }

    private static string CleanVerificationBaseUrl(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var cleaned = uri.ToString();
            return cleaned.Length > 320 ? cleaned[..320] : cleaned.TrimEnd('&', '?');
        }

        return "/signup.html";
    }

    private static string ResolveVerificationBaseUrl(string? configuredUrl, string? requestUrl)
    {
        var configured = CleanPublicSignupUrl(configuredUrl);
        return string.IsNullOrWhiteSpace(configured)
            ? CleanVerificationBaseUrl(requestUrl)
            : configured;
    }

    private static string CleanPublicSignupUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        var cleaned = uri.ToString();
        return cleaned.Length > 320 ? cleaned[..320] : cleaned.TrimEnd('&', '?');
    }

    private static string CleanEmailLogoUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SignupEmailSettings.DefaultLogoUrl;
        }

        var cleaned = value.Trim();

        if (cleaned.Contains('\0', StringComparison.Ordinal)
            || ContainsPathTraversal(cleaned)
            || cleaned.Length > 320)
        {
            return SignupEmailSettings.DefaultLogoUrl;
        }

        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (cleaned.StartsWith('/'))
        {
            return cleaned;
        }

        return SignupEmailSettings.DefaultLogoUrl;
    }

    private static string CleanPublicAssetUrl(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = value.Trim();

        if (cleaned.Contains('\0', StringComparison.Ordinal)
            || ContainsPathTraversal(cleaned)
            || cleaned.Length > 320)
        {
            return fallback;
        }

        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps
                ? absolute.ToString()
                : fallback;
        }

        return cleaned.StartsWith("/", StringComparison.Ordinal) ? cleaned : fallback;
    }

    private static string CleanLoginButtonUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim();

        if (cleaned.Contains('\0', StringComparison.Ordinal)
            || ContainsPathTraversal(cleaned)
            || cleaned.Length > 320
            || cleaned.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps
                ? absolute.ToString()
                : string.Empty;
        }

        return cleaned.Contains(':', StringComparison.Ordinal) ? string.Empty : cleaned;
    }

    private static string CleanHexColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = value.Trim();
        if (cleaned.Length != 7 || cleaned[0] != '#')
        {
            return fallback;
        }

        return cleaned.Skip(1).All(Uri.IsHexDigit) ? cleaned.ToLowerInvariant() : fallback;
    }

    private static bool ContainsPathTraversal(string value)
    {
        var current = value;

        for (var index = 0; index < 3; index++)
        {
            if (current.Contains("..", StringComparison.Ordinal)
                || current.Contains('\\', StringComparison.Ordinal)
                || current.Contains('\0', StringComparison.Ordinal)
                || current.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var decoded = Uri.UnescapeDataString(current);
            if (string.Equals(decoded, current, StringComparison.Ordinal))
            {
                return false;
            }

            current = decoded;
        }

        return current.Contains("..", StringComparison.Ordinal);
    }

    private static string? ValidatePassword(string? password, string? confirmPassword)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Enter a password.";
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return "Passwords do not match.";
        }

        if (password.Length < 8)
        {
            return "Use at least 8 characters for the password.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "Use at least one letter and one number in the password.";
        }

        return null;
    }

    private static SignupResponse Fail(string message) => new()
    {
        Message = message,
        Ok = false
    };

    private static SignupInviteValidationResponse Invalid(string message) => new()
    {
        Message = message,
        Ok = false,
        Valid = false
    };

    private static string RedactEmail(string email)
    {
        var parts = email.Split('@', 2);

        if (parts.Length != 2 || parts[0].Length <= 2)
        {
            return "***";
        }

        return $"{parts[0][0]}***{parts[0][^1]}@{parts[1]}";
    }

    private static string CleanText(string? value, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = value.Trim();

        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }

    private static string CleanIdentifier(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var cleaned = new string(source
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('-');

        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned[..Math.Min(cleaned.Length, 96)];
    }

    private static List<string> CleanStringList(IEnumerable<string>? values, int maxItems)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => value.Length <= 96)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList() ?? [];
    }

    private static Guid[] CleanGuidArray(IEnumerable<string> values)
    {
        return values
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
    }
}

