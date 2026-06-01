using Jellyfin.Plugin.Signup.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Signup;

/// <summary>
/// Registers Jellyfin Signup services.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ISignupService, SignupService>();
        serviceCollection.AddHostedService<SignupCleanupService>();
        serviceCollection.AddHostedService<SignupFileTransformationRegistrationService>();
    }
}
