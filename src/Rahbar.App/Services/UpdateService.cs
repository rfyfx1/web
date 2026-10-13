using Serilog;
using Velopack;
using Velopack.Sources;

namespace Rahbar.App.Services;

/// <summary>Velopack auto-update. The update location is a setting (a GitHub repo or a folder/URL with releases).</summary>
internal static class UpdateService
{
    public static async Task<string> CheckAndApplyAsync(string? updateUrl)
    {
        if (string.IsNullOrWhiteSpace(updateUrl))
            return "Updates aren't set up yet. Ask your admin for the update address and put it in Settings.";
        try
        {
            var mgr = updateUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(updateUrl.Trim(), null, false))
                : new UpdateManager(updateUrl.Trim());
            if (!mgr.IsInstalled)
                return "Updates only work when Rahbar was installed with RahbarSetup.exe.";

            var update = await mgr.CheckForUpdatesAsync();
            if (update is null) return $"You have the latest version ({mgr.CurrentVersion}).";

            await mgr.DownloadUpdatesAsync(update);
            Log.Information("Applying update {Version}", update.TargetFullRelease.Version);
            mgr.ApplyUpdatesAndRestart(update);
            return "Updating…";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            return "Couldn't check for updates: " + ex.Message;
        }
    }
}
