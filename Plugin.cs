using Jellyfin.Plugin.Signup.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Signup;

/// <summary>
/// Invite-based signup plugin for Jellyfin.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="xmlSerializer">Jellyfin XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Jellyfin Signup";

    /// <inheritdoc />
    public override string Description => "Adds invite-based public account signup, email verification, and password reset to Jellyfin.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b8d1fc5a-8f88-4f7c-b8fc-5abf0a66de72");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "jellyfin-signup",
                DisplayName = "Signup",
                EmbeddedResourcePath = "Jellyfin.Plugin.Signup.Pages.config.html",
                EnableInMainMenu = true,
                MenuIcon = "person_add",
                MenuSection = "server"
            },
            new PluginPageInfo
            {
                Name = "jellyfin-signup.js",
                EmbeddedResourcePath = "Jellyfin.Plugin.Signup.Pages.config.js"
            }
        ];
    }
}
