using AppMigrator.UI.Models;
using Microsoft.Win32;

namespace AppMigrator.UI.Services;

public sealed class AppDiscoveryService
{
    private readonly KnownRuleRepository _ruleRepository;

    public AppDiscoveryService(KnownRuleRepository ruleRepository)
    {
        _ruleRepository = ruleRepository;
    }

    public Task<List<DiscoveredApp>> DiscoverAsync(IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            var results = new List<DiscoveredApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var roots = new[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64),
                (RegistryHive.LocalMachine, RegistryView.Registry32),
                (RegistryHive.CurrentUser, RegistryView.Registry64),
                (RegistryHive.CurrentUser, RegistryView.Registry32)
            };

            foreach (var (hive, view) in roots)
            {
                ReadUninstallEntries(results, seen, hive, view, progress);
            }

            return results
                .OrderByDescending(app => app.Supported)
                .ThenBy(app => app.DisplayName)
                .ToList();
        });
    }

    private void ReadUninstallEntries(List<DiscoveredApp> results, HashSet<string> seen, RegistryHive hive, RegistryView view, IProgress<string>? progress)
    {
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(uninstallPath);

            if (uninstallKey is null)
            {
                return;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var appKey = uninstallKey.OpenSubKey(subKeyName);
                if (appKey is null)
                {
                    continue;
                }

                var displayName = appKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var systemComponent = appKey.GetValue("SystemComponent");
                if (systemComponent is int intValue && intValue == 1)
                {
                    continue;
                }

                var releaseType = appKey.GetValue("ReleaseType") as string;
                if (string.Equals(releaseType, "Hotfix", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(releaseType, "Security Update", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(releaseType, "Update Rollup", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var publisher = appKey.GetValue("Publisher") as string ?? string.Empty;
                var version = appKey.GetValue("DisplayVersion") as string ?? string.Empty;
                var installLocation = appKey.GetValue("InstallLocation") as string ?? string.Empty;
                var uninstallString = appKey.GetValue("UninstallString") as string ?? string.Empty;

                var identity = $"{displayName}|{version}|{installLocation}";
                if (!seen.Add(identity))
                {
                    continue;
                }

                var matchedRule = _ruleRepository.Match(displayName);

                results.Add(new DiscoveredApp
                {
                    DisplayName = displayName,
                    Publisher = publisher,
                    Version = version,
                    InstallLocation = installLocation,
                    UninstallString = uninstallString,
                    Category = matchedRule?.Category ?? "Unknown",
                    RestoreStrategy = matchedRule?.RestoreStrategy ?? "unsupported",
                    RuleId = matchedRule?.Id ?? string.Empty,
                    WingetId = matchedRule?.WingetId,
                    Supported = matchedRule?.Supported ?? false,
                    Confidence = matchedRule?.Confidence ?? 0.15,
                    Notes = matchedRule is null
                        ? "No built-in migration rule yet."
                        : string.Join(" ", matchedRule.Notes)
                });

                progress?.Report($"Found: {displayName}");
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"Discovery warning ({hive}/{view}): {ex.Message}");
        }
    }
}
