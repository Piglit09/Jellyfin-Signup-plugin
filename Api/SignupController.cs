using Jellyfin.Plugin.Signup.Models;
using Jellyfin.Plugin.Signup.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Signup.Api;

/// <summary>
/// Native Jellyfin signup and invite API.
/// </summary>
[ApiController]
[Authorize]
[Route("Signup/v1")]
public sealed class SignupController : BaseJellyfinApiController
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<SignupController> _logger;
    private readonly ISignupService _signupService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignupController"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="signupService">Signup service.</param>
    /// <param name="authorizationService">Authorization service.</param>
    public SignupController(
        ILogger<SignupController> logger,
        ISignupService signupService,
        IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
        _logger = logger;
        _signupService = signupService;
    }

    /// <summary>
    /// Gets public login page signup button settings.
    /// </summary>
    /// <returns>Login page signup button settings.</returns>
    [AllowAnonymous]
    [HttpGet("public/login-button")]
    [HttpGet("~/signup/public/login-button")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SignupLoginButtonPublicResponse> GetPublicLoginButton()
    {
        return Ok(_signupService.GetPublicLoginButton());
    }

    /// <summary>
    /// Gets public signup page settings.
    /// </summary>
    /// <returns>Public signup page settings.</returns>
    [AllowAnonymous]
    [HttpGet("public/settings")]
    [HttpGet("~/signup/public/settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SignupPublicSettingsResponse> GetPublicSettings()
    {
        return Ok(_signupService.GetPublicSettings());
    }

    /// <summary>
    /// Validates an invite code before signup.
    /// </summary>
    /// <param name="request">Invite validation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Invite validation result.</returns>
    [AllowAnonymous]
    [HttpPost("validate-invite")]
    [HttpPost("~/signup/validate-invite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignupInviteValidationResponse>> ValidateInvite(
        [FromBody] SignupValidateInviteRequest? request,
        CancellationToken cancellationToken)
    {
        return Ok(await _signupService
            .ValidateInviteAsync(request?.InviteCode, GetRemoteAddress(), cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Creates a Jellyfin local user from native Jellyfin signup.
    /// </summary>
    /// <param name="request">Create user request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Create user result.</returns>
    [AllowAnonymous]
    [HttpPost("create-user")]
    [HttpPost("~/signup/create-user")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignupResponse>> CreateUser(
        [FromBody] SignupCreateUserRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new SignupErrorResponse("JELLYFIN_SIGNUP_EMPTY_PAYLOAD", "Signup details are required."));
        }

        return Ok(await _signupService
            .CreateUserAsync(request, GetRemoteAddress(), cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Verifies a pending signup email link and finalizes user creation.
    /// </summary>
    /// <param name="request">Verification request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verification result.</returns>
    [AllowAnonymous]
    [HttpPost("verify-email")]
    [HttpPost("~/signup/verify-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignupResponse>> VerifyEmail(
        [FromBody] SignupEmailVerificationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new SignupErrorResponse("JELLYFIN_SIGNUP_VERIFY_EMPTY_PAYLOAD", "Email verification token is required."));
        }

        return Ok(await _signupService
            .VerifyEmailAsync(request, GetRemoteAddress(), cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Sends a password reset code by email.
    /// </summary>
    /// <param name="request">Password reset request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reset request result.</returns>
    [AllowAnonymous]
    [HttpPost("password-reset/request")]
    [HttpPost("~/signup/password-reset/request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignupResponse>> RequestPasswordReset(
        [FromBody] SignupPasswordResetRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new SignupErrorResponse("JELLYFIN_SIGNUP_RESET_EMPTY_PAYLOAD", "Email is required."));
        }

        return Ok(await _signupService
            .RequestPasswordResetAsync(request, GetRemoteAddress(), cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Confirms a password reset code and sets a new password.
    /// </summary>
    /// <param name="request">Password reset confirmation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reset confirmation result.</returns>
    [AllowAnonymous]
    [HttpPost("password-reset/confirm")]
    [HttpPost("~/signup/password-reset/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignupResponse>> ConfirmPasswordReset(
        [FromBody] SignupPasswordResetConfirmRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new SignupErrorResponse("JELLYFIN_SIGNUP_RESET_CONFIRM_EMPTY_PAYLOAD", "Reset code and new password are required."));
        }

        return Ok(await _signupService
            .ConfirmPasswordResetAsync(request, GetRemoteAddress(), cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Gets native Jellyfin signup settings for admins.
    /// </summary>
    /// <returns>Signup settings.</returns>
    [HttpGet("admin/settings")]
    [HttpGet("~/signup/admin/settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignupAdminSettingsResponse>> GetSettings()
    {
        if (!await CanAdminAsync().ConfigureAwait(false))
        {
            return ForbidSignupAdmin();
        }

        return Ok(_signupService.GetAdminSettings());
    }

    /// <summary>
    /// Saves native Jellyfin signup settings for admins.
    /// </summary>
    /// <param name="request">Settings patch.</param>
    /// <returns>Saved settings.</returns>
    [HttpPost("admin/settings")]
    [HttpPost("~/signup/admin/settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignupAdminSettingsResponse>> SaveSettings([FromBody] SignupAdminSettingsPatch? request)
    {
        if (!await CanAdminAsync().ConfigureAwait(false))
        {
            return ForbidSignupAdmin();
        }

        if (request is null)
        {
            return BadRequest(new SignupErrorResponse("JELLYFIN_SIGNUP_SETTINGS_EMPTY_PAYLOAD", "Signup settings are required."));
        }

        return Ok(_signupService.SaveAdminSettings(request));
    }

    /// <summary>
    /// Sends a signup engine test email for admins.
    /// </summary>
    /// <param name="request">Test email request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Test result.</returns>
    [HttpPost("admin/email/test")]
    [HttpPost("~/signup/admin/email/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignupResponse>> SendTestEmail(
        [FromBody] SignupTestEmailRequest? request,
        CancellationToken cancellationToken)
    {
        if (!await CanAdminAsync().ConfigureAwait(false))
        {
            return ForbidSignupAdmin();
        }

        return Ok(await _signupService
            .SendTestEmailAsync(request ?? new SignupTestEmailRequest(), cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Gets native Jellyfin signup invites for admins.
    /// </summary>
    /// <returns>Invite list.</returns>
    [HttpGet("admin/invites")]
    [HttpGet("~/signup/admin/invites")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignupInviteListResponse>> GetInvites()
    {
        if (!await CanAdminAsync().ConfigureAwait(false))
        {
            return ForbidSignupAdmin();
        }

        return Ok(_signupService.GetAdminInvites());
    }

    /// <summary>
    /// Creates a native Jellyfin signup invite.
    /// </summary>
    /// <param name="request">Create invite request.</param>
    /// <returns>Created invite and one-time clear invite code.</returns>
    [HttpPost("admin/invites")]
    [HttpPost("~/signup/admin/invites")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignupInviteDto>> CreateInvite([FromBody] SignupCreateInviteRequest? request)
    {
        if (!await CanAdminAsync().ConfigureAwait(false))
        {
            return ForbidSignupAdmin();
        }

        if (request is null)
        {
            return BadRequest(new SignupErrorResponse("JELLYFIN_SIGNUP_INVITE_EMPTY_PAYLOAD", "Invite settings are required."));
        }

        return Ok(_signupService.CreateInvite(request, User?.Identity?.Name ?? "admin"));
    }

    /// <summary>
    /// Disables a native Jellyfin signup invite.
    /// </summary>
    /// <param name="id">Invite id.</param>
    /// <returns>Disabled invite.</returns>
    [HttpPost("admin/invites/{id}/disable")]
    [HttpPost("~/signup/admin/invites/{id}/disable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SignupInviteDto>> DisableInvite(string id)
    {
        if (!await CanAdminAsync().ConfigureAwait(false))
        {
            return ForbidSignupAdmin();
        }

        return Ok(_signupService.DisableInvite(id));
    }

    /// <summary>
    /// Deletes a native Jellyfin signup invite.
    /// </summary>
    /// <param name="id">Invite id.</param>
    /// <returns>No content.</returns>
    [HttpDelete("admin/invites/{id}")]
    [HttpDelete("~/signup/admin/invites/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteInvite(string id)
    {
        if (!await CanAdminAsync().ConfigureAwait(false))
        {
            return ForbidSignupAdmin();
        }

        _signupService.DeleteInvite(id);
        return NoContent();
    }

    private string GetRemoteAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private async Task<bool> CanAdminAsync()
    {
        try
        {
            var result = await _authorizationService
                .AuthorizeAsync(User, Policies.RequiresElevation)
                .ConfigureAwait(false);

            return result.Succeeded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jellyfin signup admin authorization check failed.");
            return false;
        }
    }

    private ActionResult ForbidSignupAdmin()
    {
        return StatusCode(
            StatusCodes.Status403Forbidden,
            new SignupErrorResponse("JELLYFIN_SIGNUP_ADMIN_FORBIDDEN", "Only Jellyfin administrators can manage Jellyfin signup invites."));
    }
}

