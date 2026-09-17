using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace CodePrintManager.Desktop;

/// <summary>
/// Checks for application updates via the public GitHub releases repo
/// and manages the download/apply lifecycle with a safety gate for active print jobs.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager _updateManager;
    private readonly IPrintJobService _jobService;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(IPrintJobService jobService, ILogger<UpdateService> logger)
    {
        _jobService = jobService;
        _logger = logger;

        // Public releases repo — no token needed.
        var source = new GithubSource(
            "https://github.com/AniC12/codeprintmanager-releases", null, false);
        _updateManager = new UpdateManager(source);
    }

    /// <summary>
    /// False when running via <c>dotnet run</c> (not installed via Velopack).
    /// Update checks are skipped in this case.
    /// </summary>
    public bool IsInstalled => _updateManager.IsInstalled;

    /// <summary>
    /// Checks the releases repo for a newer version.
    /// Returns null if no update is available or the app is not installed via Velopack.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        if (!IsInstalled)
        {
            _logger.LogDebug("App is not installed via Velopack — skipping update check");
            return null;
        }

        try
        {
            _logger.LogInformation("Checking for updates...");
            var update = await _updateManager.CheckForUpdatesAsync();

            if (update != null)
                _logger.LogInformation("Update available: {Version}", update.TargetFullRelease.Version);
            else
                _logger.LogInformation("No updates available");

            return update;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
            return null;
        }
    }

    /// <summary>
    /// Downloads the specified update. The <paramref name="progress"/> callback
    /// receives values from 0 to 100.
    /// </summary>
    public async Task DownloadUpdateAsync(UpdateInfo update, Action<int>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Downloading update {Version}...", update.TargetFullRelease.Version);
        await _updateManager.DownloadUpdatesAsync(update, progress, ct);
        _logger.LogInformation("Update {Version} downloaded", update.TargetFullRelease.Version);
    }

    /// <summary>
    /// Returns true if there are no active print jobs (Preparing, Ready, Printing, Paused)
    /// and it is safe to restart the application for an update.
    /// </summary>
    public async Task<bool> CanApplyUpdateAsync()
    {
        var active = await _jobService.GetActiveJobsAsync();
        return active.Count == 0;
    }

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// Caller must verify <see cref="CanApplyUpdateAsync"/> first.
    /// </summary>
    public void ApplyAndRestart(UpdateInfo update)
    {
        _logger.LogInformation("Applying update {Version} and restarting...", update.TargetFullRelease.Version);
        _updateManager.ApplyUpdatesAndRestart(update);
    }
}
