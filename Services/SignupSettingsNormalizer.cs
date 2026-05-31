using Jellyfin.Plugin.Signup.Configuration;

namespace Jellyfin.Plugin.Signup.Services;

/// <summary>
/// Normalizes signup settings and preserves safe defaults for older installs.
/// </summary>
public static class SignupSettingsNormalizer
{
    /// <summary>
    /// Normalizes a plugin configuration in place.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <returns>Normalized plugin configuration.</returns>
    public static PluginConfiguration Normalize(PluginConfiguration configuration)
    {
        configuration.SignupSettings ??= new SignupSettings();
        NormalizeSignupSettings(configuration.SignupSettings);
        return configuration;
    }

    private static void NormalizeSignupSettings(SignupSettings settings)
    {
        settings.FailedAttemptLimit = Clamp(settings.FailedAttemptLimit, 3, 30);
        settings.LockoutMinutes = Clamp(settings.LockoutMinutes, 1, 120);
        settings.DefaultPolicyPresetId = CleanIdentifier(settings.DefaultPolicyPresetId, "standard");
        settings.DefaultEnabledFolderIds = CleanStringList(settings.DefaultEnabledFolderIds, 120);
        settings.LoginButtonSettings ??= new SignupLoginButtonSettings();
        NormalizeSignupLoginButtonSettings(settings.LoginButtonSettings);
        settings.EmailSettings ??= new SignupEmailSettings();
        NormalizeSignupEmailSettings(settings.EmailSettings);
        settings.AppearanceSettings ??= new SignupAppearanceSettings();
        NormalizeSignupAppearanceSettings(settings.AppearanceSettings);

        settings.PolicyPresets = (settings.PolicyPresets ?? [])
            .Where(preset => preset is not null)
            .Select(NormalizeSignupPolicyPreset)
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Id))
            .DistinctBy(preset => preset.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.PolicyPresets.Count == 0)
        {
            settings.PolicyPresets = SignupSettings.CreateDefaultPolicyPresets();
        }

        settings.Invites = (settings.Invites ?? [])
            .Where(invite => invite is not null && !string.IsNullOrWhiteSpace(invite.Id) && !string.IsNullOrWhiteSpace(invite.CodeHash))
            .Select(NormalizeSignupInvite)
            .DistinctBy(invite => invite.Id, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        settings.Users = (settings.Users ?? [])
            .Where(user => user is not null && !string.IsNullOrWhiteSpace(user.Username) && !string.IsNullOrWhiteSpace(user.Email))
            .Select(NormalizeSignupUser)
            .DistinctBy(user => user.Email, StringComparer.OrdinalIgnoreCase)
            .Take(1000)
            .ToList();

        MigrateSignupInviteUsageRecords(settings);

        var pendingCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        settings.PendingVerifications = (settings.PendingVerifications ?? [])
            .Where(pending => pending is not null
                && !string.IsNullOrWhiteSpace(pending.Id)
                && !string.IsNullOrWhiteSpace(pending.Username)
                && !string.IsNullOrWhiteSpace(pending.Email)
                && !string.IsNullOrWhiteSpace(pending.ProtectedPassword)
                && !string.IsNullOrWhiteSpace(pending.TokenHash)
                && pending.ExpiresAtUtc > pendingCutoff)
            .Select(NormalizeSignupPendingVerification)
            .DistinctBy(pending => pending.Email, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        var resetCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        settings.PasswordResetCodes = (settings.PasswordResetCodes ?? [])
            .Where(code => code is not null
                && !string.IsNullOrWhiteSpace(code.Email)
                && !string.IsNullOrWhiteSpace(code.Username)
                && !string.IsNullOrWhiteSpace(code.CodeHash)
                && code.ExpiresAtUtc > resetCutoff)
            .Select(NormalizeSignupResetCode)
            .TakeLast(250)
            .ToList();

        var rateLimitCutoff = DateTimeOffset.UtcNow.AddHours(-6);
        settings.RateLimits = (settings.RateLimits ?? [])
            .Where(record => record is not null
                && !string.IsNullOrWhiteSpace(record.Action)
                && !string.IsNullOrWhiteSpace(record.KeyHash)
                && ((record.LockedUntilUtc is not null && record.LockedUntilUtc > DateTimeOffset.UtcNow)
                    || record.WindowStartedAtUtc > rateLimitCutoff))
            .Select(NormalizeSignupRateLimit)
            .TakeLast(1000)
            .ToList();
    }

    private static void NormalizeSignupLoginButtonSettings(SignupLoginButtonSettings settings)
    {
        settings.Text = CleanText(settings.Text, "Create Account", 40);
        settings.TargetUrl = CleanLoginButtonUrl(settings.TargetUrl);
    }

    private static void NormalizeSignupEmailSettings(SignupEmailSettings settings)
    {
        settings.SmtpHost = CleanText(settings.SmtpHost, string.Empty, 160);
        settings.SmtpPort = Clamp(settings.SmtpPort, 1, 65535);
        settings.Username = CleanSecret(settings.Username);
        settings.Password = CleanSecret(settings.Password);
        settings.FromAddress = CleanText(settings.FromAddress, string.Empty, 160).ToLowerInvariant();
        settings.FromName = CleanText(settings.FromName, "Jellyfin", 80);
        settings.LogoUrl = CleanPublicAssetUrl(settings.LogoUrl, SignupEmailSettings.DefaultLogoUrl);
        settings.PublicSignupUrl = CleanPublicSignupUrl(settings.PublicSignupUrl);
        settings.ResetSubject = CleanText(settings.ResetSubject, "Your Jellyfin password reset code", 120);
        settings.ResetCodeMinutes = Clamp(settings.ResetCodeMinutes, 5, 120);
        settings.VerificationSubject = CleanText(settings.VerificationSubject, "Verify your Jellyfin email", 120);
        settings.VerificationLinkMinutes = Clamp(settings.VerificationLinkMinutes, 10, 1440);
    }

    private static void NormalizeSignupAppearanceSettings(SignupAppearanceSettings settings)
    {
        settings.PageTitle = CleanText(settings.PageTitle, "Jellyfin Signup", 80);
        settings.Heading = CleanText(settings.Heading, "Jellyfin Signup", 80);
        settings.IntroText = CleanText(settings.IntroText, "Create your Jellyfin account, then verify your email before signing in.", 180);
        settings.LogoUrl = CleanPublicAssetUrl(settings.LogoUrl, string.Empty);
        settings.BackgroundColor = CleanHexColor(settings.BackgroundColor, "#08070d");
        settings.PanelColor = CleanHexColor(settings.PanelColor, "#15131d");
        settings.TextColor = CleanHexColor(settings.TextColor, "#f7f7fb");
        settings.MutedTextColor = CleanHexColor(settings.MutedTextColor, "#bbb6c8");
        settings.PrimaryColor = CleanHexColor(settings.PrimaryColor, "#00a4dc");
        settings.SecondaryColor = CleanHexColor(settings.SecondaryColor, "#aa5cc3");
    }

    private static SignupPolicyPreset NormalizeSignupPolicyPreset(SignupPolicyPreset preset)
    {
        preset.Id = CleanIdentifier(preset.Id, preset.Name);
        preset.Name = CleanText(preset.Name, "Signup Preset", 80);
        preset.EnabledFolderIds = CleanStringList(preset.EnabledFolderIds, 120);
        preset.RemoteClientBitrateLimit = Math.Max(0, preset.RemoteClientBitrateLimit);
        preset.MaxActiveSessions = Math.Max(0, preset.MaxActiveSessions);
        preset.MaxParentalRating = preset.MaxParentalRating is null ? null : Math.Max(0, preset.MaxParentalRating.Value);
        return preset;
    }

    private static SignupInvite NormalizeSignupInvite(SignupInvite invite)
    {
        invite.Id = CleanIdentifier(invite.Id, invite.Label);
        invite.CodeHash = CleanText(invite.CodeHash, string.Empty, 128);
        invite.CodePreview = CleanText(invite.CodePreview, string.Empty, 16);
        invite.Label = CleanText(invite.Label, "Invite", 80);
        invite.MaxUses = Math.Max(0, invite.MaxUses);
        invite.PolicyPresetId = CleanIdentifier(invite.PolicyPresetId, "standard");
        invite.EnabledFolderIds = CleanStringList(invite.EnabledFolderIds, 120);
        invite.FailedAttempts = Math.Max(0, invite.FailedAttempts);
        invite.UsageRecords = (invite.UsageRecords ?? [])
            .Where(record => record is not null && !string.IsNullOrWhiteSpace(record.Username))
            .Take(500)
            .ToList();
        return invite;
    }

    private static SignupUserRecord NormalizeSignupUser(SignupUserRecord user)
    {
        user.UserId = CleanText(user.UserId, string.Empty, 64);
        user.Username = CleanText(user.Username, string.Empty, 80);
        user.Email = CleanText(user.Email, string.Empty, 160).ToLowerInvariant();
        user.PolicyPresetId = CleanIdentifier(user.PolicyPresetId, "standard");
        user.RemoteAddress = CleanText(user.RemoteAddress, string.Empty, 80);
        return user;
    }

    private static SignupPendingVerification NormalizeSignupPendingVerification(SignupPendingVerification pending)
    {
        pending.Id = CleanIdentifier(pending.Id, "pending");
        pending.Username = CleanText(pending.Username, string.Empty, 80);
        pending.Email = CleanText(pending.Email, string.Empty, 160).ToLowerInvariant();
        pending.ProtectedPassword = CleanText(pending.ProtectedPassword, string.Empty, 4096);
        pending.TokenHash = CleanText(pending.TokenHash, string.Empty, 128);
        pending.InviteId = CleanIdentifier(pending.InviteId, string.Empty);
        pending.PolicyPresetId = CleanIdentifier(pending.PolicyPresetId, "standard");
        pending.EnabledFolderIds = CleanStringList(pending.EnabledFolderIds, 120);
        pending.RemoteAddress = CleanText(pending.RemoteAddress, string.Empty, 80);
        return pending;
    }

    private static SignupPasswordResetCode NormalizeSignupResetCode(SignupPasswordResetCode code)
    {
        code.Email = CleanText(code.Email, string.Empty, 160).ToLowerInvariant();
        code.Username = CleanText(code.Username, string.Empty, 80);
        code.CodeHash = CleanText(code.CodeHash, string.Empty, 128);
        code.FailedAttempts = Math.Max(0, code.FailedAttempts);
        code.RemoteAddress = CleanText(code.RemoteAddress, string.Empty, 80);
        return code;
    }

    private static SignupRateLimitRecord NormalizeSignupRateLimit(SignupRateLimitRecord record)
    {
        record.Action = CleanIdentifier(record.Action, "signup");
        record.KeyHash = CleanText(record.KeyHash, string.Empty, 128);
        record.Count = Math.Max(0, record.Count);
        return record;
    }

    private static void MigrateSignupInviteUsageRecords(SignupSettings settings)
    {
        foreach (var record in settings.Invites.SelectMany(invite => invite.UsageRecords))
        {
            if (string.IsNullOrWhiteSpace(record.Email)
                || settings.Users.Any(user => string.Equals(user.Email, record.Email, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            settings.Users.Add(new SignupUserRecord
            {
                CreatedAtUtc = record.UsedAtUtc,
                Email = CleanText(record.Email, string.Empty, 160).ToLowerInvariant(),
                PolicyPresetId = "standard",
                RemoteAddress = record.RemoteAddress,
                UserId = record.UserId,
                Username = record.Username
            });
        }
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Clamp(value, minimum, maximum);
    }

    private static string CleanText(string? value, string fallback, int maxLength = 16)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = value.Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }

    private static string CleanPublicAssetUrl(string? value, string fallback)
    {
        var cleaned = CleanText(value, fallback, 320);
        var decoded = cleaned;

        for (var index = 0; index < 3; index++)
        {
            if (decoded.Contains('\0', StringComparison.Ordinal)
                || decoded.Contains("..", StringComparison.Ordinal)
                || decoded.Contains('\\', StringComparison.Ordinal)
                || decoded.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            var next = Uri.UnescapeDataString(decoded);
            if (string.Equals(next, decoded, StringComparison.Ordinal))
            {
                break;
            }

            decoded = next;
        }

        return cleaned;
    }

    private static string CleanPublicSignupUrl(string? value)
    {
        var cleaned = CleanText(value, string.Empty, 320);
        if (string.IsNullOrWhiteSpace(cleaned)
            || cleaned.Contains('\0', StringComparison.Ordinal)
            || cleaned.Contains('\\', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        return uri.ToString().TrimEnd('&', '?');
    }

    private static string CleanLoginButtonUrl(string? value)
    {
        var cleaned = CleanText(value, string.Empty, 320);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var decoded = cleaned;
        for (var index = 0; index < 3; index++)
        {
            if (decoded.Contains('\0', StringComparison.Ordinal)
                || decoded.Contains('\\', StringComparison.Ordinal)
                || decoded.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || decoded.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var next = Uri.UnescapeDataString(decoded);
            if (string.Equals(next, decoded, StringComparison.Ordinal))
            {
                break;
            }

            decoded = next;
        }

        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.Scheme is "http" or "https" ? cleaned : string.Empty;
        }

        return cleaned.StartsWith("/", StringComparison.Ordinal) || !cleaned.Contains(':', StringComparison.Ordinal)
            ? cleaned
            : string.Empty;
    }

    private static string CleanHexColor(string? value, string fallback)
    {
        var cleaned = CleanText(value, fallback, 16);
        if (cleaned.Length != 7 || cleaned[0] != '#')
        {
            return fallback;
        }

        return cleaned.Skip(1).All(Uri.IsHexDigit) ? cleaned.ToLowerInvariant() : fallback;
    }

    private static string CleanIdentifier(string? value, string? fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var cleaned = new string((source ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('-');
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    private static List<string> CleanStringList(IEnumerable<string>? values, int maxItems)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => CleanText(value, string.Empty, 96))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static string? CleanSecret(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
