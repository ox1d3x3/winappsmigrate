using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AppMigrator.UI.Services;

public sealed class UpdateService
{
    public async Task<(bool Success, string Message, string? Version)> CheckForUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(AppMetadata.GitHubLatestReleaseApiUrl))
        {
            return (false, "GitHub release URL is not configured yet.", null);
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinAppsMigrator");

        try
        {
            var json = await client.GetStringAsync(AppMetadata.GitHubLatestReleaseApiUrl);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tag))
            {
                return (false, "GitHub release response did not contain a version tag.", null);
            }

            return (true, string.Equals(tag.TrimStart('v', 'V'), AppMetadata.Version, StringComparison.OrdinalIgnoreCase)
                ? "You are already on the latest release."
                : $"New release detected: {tag}", tag);
        }
        catch (Exception ex)
        {
            return (false, $"Update check failed: {ex.Message}", null);
        }
    }
}
