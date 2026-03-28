using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Services;

public sealed class PackageRestoreService
{
    private readonly KnownRuleRepository _ruleRepository;
    private readonly WinGetService _winGetService;
    private readonly ChocolateyService _chocolateyService;
    private readonly PackageManifestService _packageManifestService = new();

    public PackageRestoreService(KnownRuleRepository ruleRepository, WinGetService winGetService, ChocolateyService chocolateyService)
    {
        _ruleRepository = ruleRepository;
        _winGetService = winGetService;
        _chocolateyService = chocolateyService;
    }

    public async Task<PackageRestoreExecutionResult> RestoreFromXmlAsync(string xmlPath, IProgress<MigrationProgressInfo>? progress = null, IProgress<string>? log = null)
    {
        var manifest = await _packageManifestService.ImportAsync(xmlPath);
        var requests = manifest.Packages
            .Where(package => !string.IsNullOrWhiteSpace(package.DisplayName))
            .Select(package => new PackageInstallRequest
            {
                AppId = package.AppId,
                DisplayName = package.DisplayName,
                Publisher = package.Publisher,
                Version = package.Version,
                RestoreStrategy = package.RestoreStrategy,
                WingetId = string.IsNullOrWhiteSpace(package.WingetId) ? null : package.WingetId,
                ChocolateyId = string.IsNullOrWhiteSpace(package.ChocolateyId) ? null : package.ChocolateyId
            })
            .ToList();

        return await RestorePackagesAsync(requests, progress, log);
    }

    public async Task<PackageRestoreExecutionResult> RestorePackagesAsync(IReadOnlyList<PackageInstallRequest> requests, IProgress<MigrationProgressInfo>? progress = null, IProgress<string>? log = null)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("No package entries were supplied.");
        }

        var result = new PackageRestoreExecutionResult();
        var discovery = new AppDiscoveryService(_ruleRepository);
        var installedApps = await discovery.DiscoverAsync();
        var wingetAvailable = _winGetService.IsAvailable();
        var chocolateyAvailable = _chocolateyService.IsAvailable();

        log?.Report($"Package restore started for {requests.Count} app(s).");
        log?.Report($"Package managers available - WinGet: {(wingetAvailable ? "Yes" : "No")}, Chocolatey: {(chocolateyAvailable ? "Yes" : "No")}");

        if (!wingetAvailable && !chocolateyAvailable)
        {
            throw new InvalidOperationException("Neither WinGet nor Chocolatey is available on this system. Install one package manager first, then try again.");
        }

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            Report(progress, "Package restore", $"Checking if {request.DisplayName} is already installed", request.DisplayName, index, requests.Count, false);

            if (IsAlreadyInstalled(request, installedApps))
            {
                log?.Report($"Package check: {request.DisplayName} is already installed. Skipping install.");
                result.Results.Add(new PackageInstallResult
                {
                    DisplayName = request.DisplayName,
                    Status = "AlreadyInstalled",
                    Manager = "Detected",
                    PackageId = request.WingetId ?? request.ChocolateyId ?? string.Empty,
                    Message = "Already installed on this machine."
                });
                continue;
            }

            var managerResult = await TryInstallWithAvailableManagersAsync(request, index, requests.Count, progress, log);

            if (string.Equals(managerResult.Status, "Installed", StringComparison.OrdinalIgnoreCase))
            {
                log?.Report($"Refreshing installed app inventory after installing {request.DisplayName}.");
                installedApps = await RefreshInstalledAppsAsync(discovery, progress, request, index, requests.Count);
                if (!IsAlreadyInstalled(request, installedApps))
                {
                    managerResult = new PackageInstallResult
                    {
                        DisplayName = request.DisplayName,
                        Status = "Warning",
                        Manager = managerResult.Manager,
                        PackageId = managerResult.PackageId,
                        Message = $"{managerResult.Manager} reported success, but the app could not be detected after install."
                    };
                }
            }

            result.Results.Add(managerResult);
        }

        Report(progress, "Package restore", "Package restore completed.", null, requests.Count, requests.Count, false);
        log?.Report($"Package restore completed. Installed: {result.InstalledCount}, already installed: {result.AlreadyInstalledCount}, warnings: {result.WarningCount}");
        return result;
    }

    private async Task<PackageInstallResult> TryInstallWithAvailableManagersAsync(PackageInstallRequest request, int index, int total, IProgress<MigrationProgressInfo>? progress, IProgress<string>? log)
    {
        if (_winGetService.IsAvailable())
        {
            var winGetResult = await TryInstallWithWinGetAsync(request, index, total, progress, log);
            if (string.Equals(winGetResult.Status, "Installed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(winGetResult.Status, "AlreadyInstalled", StringComparison.OrdinalIgnoreCase))
            {
                return winGetResult;
            }

            log?.Report($"WinGet could not complete {request.DisplayName}. {(string.IsNullOrWhiteSpace(winGetResult.Message) ? string.Empty : winGetResult.Message)}".Trim());
        }

        if (_chocolateyService.IsAvailable())
        {
            return await TryInstallWithChocolateyAsync(request, index, total, progress, log);
        }

        return new PackageInstallResult
        {
            DisplayName = request.DisplayName,
            Status = "Warning",
            Manager = "None",
            PackageId = string.Empty,
            Message = "No available package manager could install this app."
        };
    }

    private async Task<PackageInstallResult> TryInstallWithWinGetAsync(PackageInstallRequest request, int index, int total, IProgress<MigrationProgressInfo>? progress, IProgress<string>? log)
    {
        var packageId = request.WingetId;
        if (string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(request.AppId))
        {
            packageId = _ruleRepository.GetById(request.AppId)?.WingetId;
        }

        if (string.IsNullOrWhiteSpace(packageId))
        {
            Report(progress, "Package restore", $"Searching WinGet for {request.DisplayName}", request.DisplayName, index, total, false);
            log?.Report($"Searching WinGet for {request.DisplayName}.");
            var lookup = await _winGetService.FindPackageIdByNameAsync(request.DisplayName, new Progress<string>(line => log?.Report($"WinGet: {line}")));
            if (!lookup.Found || string.IsNullOrWhiteSpace(lookup.PackageId))
            {
                return new PackageInstallResult
                {
                    DisplayName = request.DisplayName,
                    Status = "Warning",
                    Manager = "WinGet",
                    PackageId = string.Empty,
                    Message = string.IsNullOrWhiteSpace(lookup.Message) ? "WinGet package was not found." : lookup.Message
                };
            }

            packageId = lookup.PackageId;
            log?.Report(lookup.Message);
        }

        Report(progress, "Package restore", $"Installing {request.DisplayName} with WinGet", request.DisplayName, index, total, true);
        log?.Report($"Installing {request.DisplayName} with WinGet ({packageId}).");
        var install = await _winGetService.InstallWithRecoveryAsync(packageId, new Progress<string>(line => log?.Report($"WinGet: {line}")));
        if (!install.Succeeded)
        {
            return new PackageInstallResult
            {
                DisplayName = request.DisplayName,
                Status = "Warning",
                Manager = "WinGet",
                PackageId = packageId,
                Message = install.Message
            };
        }

        return new PackageInstallResult
        {
            DisplayName = request.DisplayName,
            Status = "Installed",
            Manager = "WinGet",
            PackageId = packageId,
            Message = string.IsNullOrWhiteSpace(install.Message) ? "Installed successfully with WinGet." : install.Message
        };
    }

    private async Task<PackageInstallResult> TryInstallWithChocolateyAsync(PackageInstallRequest request, int index, int total, IProgress<MigrationProgressInfo>? progress, IProgress<string>? log)
    {
        var packageId = request.ChocolateyId;
        if (string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(request.AppId))
        {
            packageId = _ruleRepository.GetById(request.AppId)?.ChocolateyId;
        }

        if (string.IsNullOrWhiteSpace(packageId))
        {
            Report(progress, "Package restore", $"Searching Chocolatey for {request.DisplayName}", request.DisplayName, index, total, false);
            log?.Report($"Searching Chocolatey for {request.DisplayName}.");
            var lookup = await _chocolateyService.FindPackageIdByNameAsync(request.DisplayName, new Progress<string>(line => log?.Report($"Chocolatey: {line}")));
            if (!lookup.Found || string.IsNullOrWhiteSpace(lookup.PackageId))
            {
                return new PackageInstallResult
                {
                    DisplayName = request.DisplayName,
                    Status = "Warning",
                    Manager = "Chocolatey",
                    PackageId = string.Empty,
                    Message = string.IsNullOrWhiteSpace(lookup.Message) ? "Chocolatey package was not found." : lookup.Message
                };
            }

            packageId = lookup.PackageId;
            log?.Report(lookup.Message);
        }

        Report(progress, "Package restore", $"Installing {request.DisplayName} with Chocolatey", request.DisplayName, index, total, true);
        log?.Report($"Installing {request.DisplayName} with Chocolatey ({packageId}).");
        var install = await _chocolateyService.InstallByIdAsync(packageId, new Progress<string>(line => log?.Report($"Chocolatey: {line}")));
        if (!install.Succeeded)
        {
            return new PackageInstallResult
            {
                DisplayName = request.DisplayName,
                Status = "Warning",
                Manager = "Chocolatey",
                PackageId = packageId,
                Message = install.Message
            };
        }

        return new PackageInstallResult
        {
            DisplayName = request.DisplayName,
            Status = "Installed",
            Manager = "Chocolatey",
            PackageId = packageId,
            Message = string.IsNullOrWhiteSpace(install.Message) ? "Installed successfully with Chocolatey." : install.Message
        };
    }

    private async Task<List<DiscoveredApp>> RefreshInstalledAppsAsync(AppDiscoveryService discovery, IProgress<MigrationProgressInfo>? progress, PackageInstallRequest request, int index, int total)
    {
        List<DiscoveredApp> latest = new();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Report(progress, "Package restore", $"Verifying installation for {request.DisplayName} (attempt {attempt}/3)", request.DisplayName, index, total, false);
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            latest = await discovery.DiscoverAsync();
            if (IsAlreadyInstalled(request, latest))
            {
                return latest;
            }
        }

        return latest.Count > 0 ? latest : await discovery.DiscoverAsync();
    }

    private static bool IsAlreadyInstalled(PackageInstallRequest request, IEnumerable<DiscoveredApp> installedApps)
    {
        foreach (var installed in installedApps)
        {
            if (!string.IsNullOrWhiteSpace(request.WingetId)
                && !string.IsNullOrWhiteSpace(installed.WingetId)
                && string.Equals(request.WingetId, installed.WingetId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(request.AppId)
                && string.Equals(request.AppId, installed.RuleId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(installed.DisplayName, request.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (installed.DisplayName.Contains(request.DisplayName, StringComparison.OrdinalIgnoreCase)
                || request.DisplayName.Contains(installed.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildReport(PackageRestoreExecutionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Package restore report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Tool: {AppMetadata.DisplayTitle}");
        builder.AppendLine();

        foreach (var entry in result.Results)
        {
            builder.AppendLine($"{entry.Status} | {entry.DisplayName} | Manager: {entry.Manager} | Package: {entry.PackageId} | {entry.Message}");
        }

        return builder.ToString();
    }

    private static void Report(IProgress<MigrationProgressInfo>? progress, string stage, string message, string? currentApp, int processedItems, int totalItems, bool isIndeterminate)
    {
        var percent = totalItems == 0 ? 0 : Math.Min(100d, (double)processedItems / totalItems * 100d);
        progress?.Report(new MigrationProgressInfo
        {
            Stage = stage,
            Message = message,
            CurrentApp = currentApp,
            Percent = percent,
            ProcessedItems = processedItems,
            TotalItems = totalItems,
            IsIndeterminate = isIndeterminate
        });
    }
}
