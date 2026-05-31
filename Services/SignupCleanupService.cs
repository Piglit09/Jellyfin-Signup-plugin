using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Signup.Services;

/// <summary>
/// Periodically removes expired Jellyfin signup verification and reset records.
/// </summary>
public sealed class SignupCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private readonly ILogger<SignupCleanupService> _logger;
    private readonly ISignupService _signupService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignupCleanupService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="signupService">Signup service.</param>
    public SignupCleanupService(
        ILogger<SignupCleanupService> logger,
        ISignupService signupService)
    {
        _logger = logger;
        _signupService = signupService;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                var removed = _signupService.CleanupExpiredRecords();

                if (removed > 0)
                {
                    _logger.LogInformation("Jellyfin signup cleanup removed {RemovedCount} expired records.", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Jellyfin signup cleanup failed.");
            }
        }
    }
}

