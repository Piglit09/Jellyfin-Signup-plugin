using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Signup.Services;

/// <summary>
/// Registers Jellyfin web login page transformations when File Transformation is installed.
/// </summary>
public sealed class SignupFileTransformationRegistrationService : BackgroundService
{
    private static readonly Guid LoginHtmlTransformationId = Guid.Parse("0eeaf977-6a35-4b8b-bc23-7ec78a957e0b");
    private static readonly Guid LoginJsTransformationId = Guid.Parse("d92fa14b-09cc-4a6e-912c-0f8f7d9f51e7");
    private static readonly Guid LoginBundleTransformationId = Guid.Parse("dfdc93e5-e337-4519-9155-51b289de7fc7");
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 24;
    private readonly ILogger<SignupFileTransformationRegistrationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignupFileTransformationRegistrationService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public SignupFileTransformationRegistrationService(ILogger<SignupFileTransformationRegistrationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);

        for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                if (TryRegisterTransformations())
                {
                    _logger.LogInformation("Jellyfin signup registered login page transformations.");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Jellyfin signup login page transformation registration attempt {Attempt} failed.", attempt);
            }

            await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Jellyfin signup could not find File Transformation. The public signup page still works, but native login-page button patching requires the File Transformation plugin.");
    }

    private static bool TryRegisterTransformations()
    {
        var registerMethod = FindRegisterMethod();
        if (registerMethod is null)
        {
            return false;
        }

        RegisterTransformation(
            registerMethod,
            LoginHtmlTransformationId,
            @".*controllers/session/login/index\.html$",
            nameof(SignupLoginPageTransformation.LoginHtml));
        RegisterTransformation(
            registerMethod,
            LoginJsTransformationId,
            @".*controllers/session/login/index\.js$",
            nameof(SignupLoginPageTransformation.LoginJs));
        RegisterTransformation(
            registerMethod,
            LoginBundleTransformationId,
            @".*main\.jellyfin\.bundle\.js(?:\?.*)?$",
            nameof(SignupLoginPageTransformation.LoginBundleJs));

        return true;
    }

    private static MethodInfo? FindRegisterMethod()
    {
        var fileTransformationAssembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly => assembly.FullName?.Contains(".FileTransformation", StringComparison.OrdinalIgnoreCase) == true);
        var pluginInterfaceType = fileTransformationAssembly?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");

        return pluginInterfaceType?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
    }

    private static void RegisterTransformation(MethodInfo registerMethod, Guid id, string fileNamePattern, string callbackMethod)
    {
        var payload = new
        {
            id,
            fileNamePattern,
            callbackAssembly = typeof(SignupLoginPageTransformation).Assembly.FullName,
            callbackClass = typeof(SignupLoginPageTransformation).FullName,
            callbackMethod
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadType = registerMethod.GetParameters()[0].ParameterType;
        var parseMethod = payloadType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method =>
                string.Equals(method.Name, "Parse", StringComparison.Ordinal)
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(string));
        var payloadObject = parseMethod.Invoke(null, [payloadJson]);

        registerMethod.Invoke(null, [payloadObject]);
    }
}
