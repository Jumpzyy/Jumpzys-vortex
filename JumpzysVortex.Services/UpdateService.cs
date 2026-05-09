using System.Net.Http;
using System.Text.Json;

namespace JumpzysVortex.Services;

public sealed class UpdateService
{
    public Version CurrentVersion { get; } = new(2, 2, 0);

    public async Task<UpdateCheckResult> CheckAsync(string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return new UpdateCheckResult(false, "No update manifest URL configured.", null, null);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var json = await http.GetStringAsync(manifestUrl);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
        if (manifest == null || !Version.TryParse(manifest.Version, out var latest))
            return new UpdateCheckResult(false, "Update manifest was invalid.", null, null);

        return latest > CurrentVersion
            ? new UpdateCheckResult(true, $"Update available: {latest}", manifest.DownloadUrl, latest.ToString())
            : new UpdateCheckResult(false, $"Already current: {CurrentVersion}", manifest.DownloadUrl, latest.ToString());
    }
}

public sealed record UpdateCheckResult(bool HasUpdate, string Message, string? DownloadUrl, string? LatestVersion);

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}
