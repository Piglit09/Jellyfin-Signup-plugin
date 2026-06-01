namespace Jellyfin.Plugin.Signup.Services;

/// <summary>
/// Payload passed by the File Transformation plugin.
/// </summary>
public sealed class SignupFileTransformationPayload
{
    /// <summary>Gets or sets the current served file contents.</summary>
    public string Contents { get; set; } = string.Empty;
}
