using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Signup.Configuration;

/// <summary>
/// Jellyfin Signup plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets signup, invite, email, and reset settings.
    /// </summary>
    public SignupSettings SignupSettings { get; set; } = new();
}
