using Velopack;
using Velopack.Sources;

namespace TextFix.Services;

public enum UpdateState { NotInstalled, UpToDate, Ready, Error }

public record UpdateCheckResult(UpdateState State, UpdateInfo? Info, string? Version = null, string? Error = null);

public class UpdateService
{
    private const string RepoUrl = "https://github.com/agnt-labs-oz/TextFix";
    private readonly UpdateManager _manager;

    public UpdateService()
    {
        _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "dev";

    public async Task<UpdateCheckResult> CheckAndDownloadAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
            return new UpdateCheckResult(UpdateState.NotInstalled, null);

        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
                return new UpdateCheckResult(UpdateState.UpToDate, null, _manager.CurrentVersion?.ToString());

            await _manager.DownloadUpdatesAsync(info, cancelToken: ct).ConfigureAwait(false);
            return new UpdateCheckResult(UpdateState.Ready, info, info.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateState.Error, null, Error: ex.Message);
        }
    }

    public void ApplyOnExit(UpdateInfo info) => _manager.WaitExitThenApplyUpdates(info);

    public void ApplyAndRestart(UpdateInfo info) => _manager.ApplyUpdatesAndRestart(info);
}
