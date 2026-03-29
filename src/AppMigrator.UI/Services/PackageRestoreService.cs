using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Services;

public sealed class PackageRestoreService
{
    private const double CheckPhaseWeight = 45d;
    private const double InstallPhaseWeight = 55d;

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
        ReportOverall(progress, "Package restore", "Loading XML package list", null, 1d, 0, 0, true);
        var manifest = await _packageManifestService.ImportAsync(xmlPath).ConfigureAwait(false);
        var requests = manifest.Packages
            .Where(package => !string.IsNullOrWhiteSpace(package.DisplayName))
            .Select(package => new PackageInstallRequest
            {
                AppId = package.AppId,
                DisplayName = package.DisplayName,
                Publisher = package.Publisher,
                Version = package.Version,
                RestoreStrategy = package.RestoreStrategy,
                Supported = package.Supported,
                WingetId = string.IsNullOrWhiteSpace(package.WingetId) ? null : package.WingetId,
                ChocolateyId = string.IsNullOrWhiteSpace(package.ChocolateyId) ? null : package.ChocolateyId
            })
            .Select(NormalizeRequest)
            .ToList();

        log?.Report($"Loaded XML package list with {requests.Count} app entries.");
        return await RestorePackagesAsync(requests, progress, log).ConfigureAwait(false);
    }

    public async Task<PackageRestoreExecutionResult> RestorePackagesAsync(IReadOnlyList<PackageInstallRequest> requests, IProgress<MigrationProgressInfo>? progress = null, IProgress<string>? log = null)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("No package entries were supplied.");
        }

        var normalizedRequests = requests.Select(NormalizeRequest).ToList();
        var result = new PackageRestoreExecutionResult();
        var discovery = new AppDiscoveryService(_ruleRepository);

        ReportOverall(progress, "Package restore", "Checking installed apps on this PC", null, 3d, 0, normalizedRequests.Count, true);
        var installedApps = await discovery.DiscoverAsync().ConfigureAwait(false);

        var needsWinget = normalizedRequests.Any(NeedsWingetCheck);
        var needsChocolatey = normalizedRequests.Any(NeedsChocolateyCheck);

        ReportOverall(progress, "Package restore", "Checking package managers", null, 6d, 0, normalizedRequests.Count, true);
        var wingetAvailableTask = needsWinget ? _winGetService.IsAvailableAsync() : Task.FromResult(false);
        var chocolateyAvailableTask = needsChocolatey ? _chocolateyService.IsAvailableAsync() : Task.FromResult(false);
        await Task.WhenAll(wingetAvailableTask, chocolateyAvailableTask).ConfigureAwait(false);
        var wingetAvailable = await wingetAvailableTask.ConfigureAwait(false);
        var chocolateyAvailable = await chocolateyAvailableTask.ConfigureAwait(false);

        log?.Report($"Package restore started for {normalizedRequests.Count} app(s).");
        log?.Report($"Package managers required - WinGet: {(needsWinget ? "Yes" : "No")}, Chocolatey: {(needsChocolatey ? "Yes" : "No")}");
        log?.Report($"Package managers available - WinGet: {(wingetAvailable ? "Yes" : "No")}, Chocolatey: {(chocolateyAvailable ? "Yes" : "No")}");

        if ((needsWinget || needsChocolatey) && !wingetAvailable && !chocolateyAvailable)
        {
            throw new InvalidOperationException("Neither WinGet nor Chocolatey is available on this system. Install one package manager first, then try again.");
        }

        var plans = new List<PackageInstallPlan>(normalizedRequests.Count);
        for (var index = 0; index < normalizedRequests.Count; index++)
        {
            var request = normalizedRequests[index];
            ReportCheck(progress, $"Checking apps ({index + 1}/{normalizedRequests.Count})", $"Checking {request.DisplayName}", request.DisplayName, index, normalizedRequests.Count, false);

            var plan = await EvaluateRequestAsync(request, installedApps, wingetAvailable, chocolateyAvailable, index, normalizedRequests.Count, log, progress).ConfigureAwait(false);
            plans.Add(plan);

            if (plan.ImmediateResult is not null)
            {
                result.Results.Add(plan.ImmediateResult);
            }
        }

        var installQueue = plans.Where(plan => plan.ShouldInstall).ToList();
        log?.Report($"Check complete. Found for install: {installQueue.Count}. Already installed: {result.AlreadyInstalledCount}. Skipped: {result.SkippedCount}. Not found: {result.NotFoundCount}.");
        ReportOverall(progress, "Package restore", $"Check complete. Found {installQueue.Count} installable app(s).", null, CheckPhaseWeight, normalizedRequests.Count, normalizedRequests.Count, false);

        if (installQueue.Count == 0)
        {
            ReportOverall(progress, "Package restore", "No apps needed installation after checks.", null, 100d, normalizedRequests.Count, normalizedRequests.Count, false);
            log?.Report("Package restore completed. Nothing left to install.");
            return result;
        }

        for (var installIndex = 0; installIndex < installQueue.Count; installIndex++)
        {
            var plan = installQueue[installIndex];
            var request = plan.Request;

            ReportInstall(progress, $"Installing apps ({installIndex + 1}/{installQueue.Count})", $"Installing {request.DisplayName}", request.DisplayName, installIndex, installQueue.Count, true);
            log?.Report($"Installing {request.DisplayName} using {plan.PrimaryManager} ({plan.PrimaryPackageId}).");

            var installResult = await InstallResolvedPackageAsync(plan, log).ConfigureAwait(false);
            if (string.Equals(installResult.Status, "Installed", StringComparison.OrdinalIgnoreCase))
            {
                log?.Report($"Verifying installation for {request.DisplayName}.");
                installedApps = await RefreshInstalledAppsAsync(discovery, progress, request, installIndex, installQueue.Count).ConfigureAwait(false);
                if (!IsAlreadyInstalled(request, installedApps))
                {
                    installResult = new PackageInstallResult
                    {
                        DisplayName = request.DisplayName,
                        Status = "Failed",
                        Manager = installResult.Manager,
                        PackageId = installResult.PackageId,
                        Message = $"{installResult.Manager} reported success, but the app could not be detected after install."
                    };
                }
            }

            result.Results.Add(installResult);
        }

        ReportOverall(progress, "Package restore", "Package restore completed.", null, 100d, normalizedRequests.Count, normalizedRequests.Count, false);
        log?.Report($"Package restore completed. Installed: {result.InstalledCount}, already installed: {result.AlreadyInstalledCount}, not found: {result.NotFoundCount}, skipped: {result.SkippedCount}, failed: {result.FailedCount}.");
        return result;
    }

    private static bool NeedsWingetCheck(PackageInstallRequest request)
        => request.Supported || !string.IsNullOrWhiteSpace(request.WingetId);

    private static bool NeedsChocolateyCheck(PackageInstallRequest request)
        => !string.IsNullOrWhiteSpace(request.ChocolateyId)
           || (request.Supported && string.IsNullOrWhiteSpace(request.WingetId));

    private PackageInstallRequest NormalizeRequest(PackageInstallRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AppId))
        {
            var rule = _ruleRepository.GetById(request.AppId);
            if (rule is not null)
            {
                request.WingetId ??= string.IsNullOrWhiteSpace(rule.WingetId) ? null : rule.WingetId;
                request.ChocolateyId ??= string.IsNullOrWhiteSpace(rule.ChocolateyId) ? null : rule.ChocolateyId;
                request.Supported = request.Supported || rule.Supported;
                request.RestoreStrategy = string.IsNullOrWhiteSpace(request.RestoreStrategy) ? rule.RestoreStrategy : request.RestoreStrategy;
            }
        }

        if (!request.Supported && !string.IsNullOrWhiteSpace(request.RestoreStrategy) && !string.Equals(request.RestoreStrategy, "unsupported", StringComparison.OrdinalIgnoreCase))
        {
            request.Supported = true;
        }

        return request;
    }

    private async Task<PackageInstallPlan> EvaluateRequestAsync(PackageInstallRequest request, IReadOnlyList<DiscoveredApp> installedApps, bool wingetAvailable, bool chocolateyAvailable, int index, int total, IProgress<string>? log, IProgress<MigrationProgressInfo>? progress)
    {
        if (IsAlreadyInstalled(request, installedApps))
        {
            log?.Report($"Check: {request.DisplayName} is already installed.");
            return PackageInstallPlan.FromImmediate(request, new PackageInstallResult
            {
                DisplayName = request.DisplayName,
                Status = "AlreadyInstalled",
                Manager = "Detected",
                PackageId = request.WingetId ?? request.ChocolateyId ?? string.Empty,
                Message = "Already installed on this machine."
            });
        }

        var hasKnownPackageId = !string.IsNullOrWhiteSpace(request.WingetId) || !string.IsNullOrWhiteSpace(request.ChocolateyId);
        var canSearchByName = request.Supported || hasKnownPackageId;
        if (!canSearchByName)
        {
            log?.Report($"Check: {request.DisplayName} is not supported for auto reinstall. Skipping.");
            return PackageInstallPlan.FromImmediate(request, new PackageInstallResult
            {
                DisplayName = request.DisplayName,
                Status = "Skipped",
                Manager = "None",
                PackageId = string.Empty,
                Message = "Unsupported app entry. No known package source, so it was skipped."
            });
        }

        var wingetId = request.WingetId;
        var chocolateyId = request.ChocolateyId;

        if (!string.IsNullOrWhiteSpace(wingetId))
        {
            log?.Report($"Check: found WinGet package for {request.DisplayName}: {wingetId}");
        }
        else if (wingetAvailable)
        {
            ReportCheck(progress, $"Checking apps ({index + 1}/{total})", $"Searching WinGet for {request.DisplayName}", request.DisplayName, index, total, false);
            var lookup = await _winGetService.FindPackageIdByNameAsync(request.DisplayName, null).ConfigureAwait(false);
            if (lookup.Found && !string.IsNullOrWhiteSpace(lookup.PackageId))
            {
                wingetId = lookup.PackageId;
                log?.Report($"Check: found WinGet package for {request.DisplayName}: {wingetId}");
            }
            else
            {
                log?.Report($"Check: WinGet did not find a package for {request.DisplayName}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(chocolateyId))
        {
            log?.Report($"Check: found Chocolatey package for {request.DisplayName}: {chocolateyId}");
        }
        else if ((string.IsNullOrWhiteSpace(wingetId) || !wingetAvailable) && chocolateyAvailable)
        {
            ReportCheck(progress, $"Checking apps ({index + 1}/{total})", $"Searching Chocolatey for {request.DisplayName}", request.DisplayName, index, total, false);
            var lookup = await _chocolateyService.FindPackageIdByNameAsync(request.DisplayName, null).ConfigureAwait(false);
            if (lookup.Found && !string.IsNullOrWhiteSpace(lookup.PackageId))
            {
                chocolateyId = lookup.PackageId;
                log?.Report($"Check: found Chocolatey package for {request.DisplayName}: {chocolateyId}");
            }
            else
            {
                log?.Report($"Check: Chocolatey did not find a package for {request.DisplayName}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(wingetId) || !string.IsNullOrWhiteSpace(chocolateyId))
        {
            request.WingetId = wingetId;
            request.ChocolateyId = chocolateyId;

            var plan = new PackageInstallPlan { Request = request };
            if (!string.IsNullOrWhiteSpace(wingetId) && wingetAvailable)
            {
                plan.PrimaryManager = "WinGet";
                plan.PrimaryPackageId = wingetId;
                plan.SecondaryManager = !string.IsNullOrWhiteSpace(chocolateyId) && chocolateyAvailable ? "Chocolatey" : null;
                plan.SecondaryPackageId = !string.IsNullOrWhiteSpace(chocolateyId) && chocolateyAvailable ? chocolateyId : null;
            }
            else if (!string.IsNullOrWhiteSpace(chocolateyId) && chocolateyAvailable)
            {
                plan.PrimaryManager = "Chocolatey";
                plan.PrimaryPackageId = chocolateyId;
            }
            else
            {
                return PackageInstallPlan.FromImmediate(request, new PackageInstallResult
                {
                    DisplayName = request.DisplayName,
                    Status = "Failed",
                    Manager = "None",
                    PackageId = wingetId ?? chocolateyId ?? string.Empty,
                    Message = "A package ID was found, but the required package manager is not available on this system."
                });
            }

            return plan;
        }

        return PackageInstallPlan.FromImmediate(request, new PackageInstallResult
        {
            DisplayName = request.DisplayName,
            Status = "NotFound",
            Manager = "None",
            PackageId = string.Empty,
            Message = "No WinGet or Chocolatey package could be found."
        });
    }

    private async Task<PackageInstallResult> InstallResolvedPackageAsync(PackageInstallPlan plan, IProgress<string>? log)
    {
        var primaryResult = await InstallWithManagerAsync(plan.Request, plan.PrimaryManager!, plan.PrimaryPackageId!, log).ConfigureAwait(false);
        if (string.Equals(primaryResult.Status, "Installed", StringComparison.OrdinalIgnoreCase))
        {
            return primaryResult;
        }

        if (!string.IsNullOrWhiteSpace(plan.SecondaryManager) && !string.IsNullOrWhiteSpace(plan.SecondaryPackageId))
        {
            log?.Report($"Primary install failed for {plan.Request.DisplayName}. Falling back to {plan.SecondaryManager}.");
            var secondaryResult = await InstallWithManagerAsync(plan.Request, plan.SecondaryManager!, plan.SecondaryPackageId!, log).ConfigureAwait(false);
            if (string.Equals(secondaryResult.Status, "Installed", StringComparison.OrdinalIgnoreCase))
            {
                return secondaryResult;
            }

            return new PackageInstallResult
            {
                DisplayName = plan.Request.DisplayName,
                Status = "Failed",
                Manager = $"{primaryResult.Manager} -> {secondaryResult.Manager}",
                PackageId = string.Join(" | ", new[] { primaryResult.PackageId, secondaryResult.PackageId }.Where(x => !string.IsNullOrWhiteSpace(x))),
                Message = string.Join(" | ", new[] { primaryResult.Message, secondaryResult.Message }.Where(x => !string.IsNullOrWhiteSpace(x)))
            };
        }

        primaryResult.Status = "Failed";
        return primaryResult;
    }

    private async Task<PackageInstallResult> InstallWithManagerAsync(PackageInstallRequest request, string manager, string packageId, IProgress<string>? log)
    {
        if (string.Equals(manager, "WinGet", StringComparison.OrdinalIgnoreCase))
        {
            var install = await _winGetService.InstallWithRecoveryAsync(packageId, BuildFilteredLog(log, "WinGet")).ConfigureAwait(false);
            return new PackageInstallResult
            {
                DisplayName = request.DisplayName,
                Status = install.Succeeded ? "Installed" : "Failed",
                Manager = "WinGet",
                PackageId = packageId,
                Message = string.IsNullOrWhiteSpace(install.Message)
                    ? (install.Succeeded ? "Installed successfully with WinGet." : "WinGet install failed.")
                    : install.Message
            };
        }

        var chocolateyInstall = await _chocolateyService.InstallByIdAsync(packageId, BuildFilteredLog(log, "Chocolatey")).ConfigureAwait(false);
        return new PackageInstallResult
        {
            DisplayName = request.DisplayName,
            Status = chocolateyInstall.Succeeded ? "Installed" : "Failed",
            Manager = "Chocolatey",
            PackageId = packageId,
            Message = string.IsNullOrWhiteSpace(chocolateyInstall.Message)
                ? (chocolateyInstall.Succeeded ? "Installed successfully with Chocolatey." : "Chocolatey install failed.")
                : chocolateyInstall.Message
        };
    }

    private static IProgress<string>? BuildFilteredLog(IProgress<string>? log, string prefix)
    {
        if (log is null)
        {
            return null;
        }

        string? lastLine = null;
        return new Progress<string>(line =>
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var cleaned = line.Trim();
            if (string.Equals(cleaned, lastLine, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lastLine = cleaned;
            log.Report($"{prefix}: {cleaned}");
        });
    }

    private async Task<List<DiscoveredApp>> RefreshInstalledAppsAsync(AppDiscoveryService discovery, IProgress<MigrationProgressInfo>? progress, PackageInstallRequest request, int installIndex, int installTotal)
    {
        List<DiscoveredApp> latest = new();
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            ReportInstall(progress, $"Installing apps ({Math.Min(installIndex + 1, installTotal)}/{installTotal})", $"Verifying {request.DisplayName} (attempt {attempt}/2)", request.DisplayName, installIndex, installTotal, false);
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt)).ConfigureAwait(false);
            latest = await discovery.DiscoverAsync().ConfigureAwait(false);
            if (IsAlreadyInstalled(request, latest))
            {
                return latest;
            }
        }

        return latest.Count > 0 ? latest : await discovery.DiscoverAsync().ConfigureAwait(false);
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
        builder.AppendLine($"Installed: {result.InstalledCount} | Already installed: {result.AlreadyInstalledCount} | Not found: {result.NotFoundCount} | Skipped: {result.SkippedCount} | Failed: {result.FailedCount}");
        builder.AppendLine();

        foreach (var section in new[] { "Installed", "AlreadyInstalled", "NotFound", "Skipped", "Failed" })
        {
            var entries = result.Results.Where(entry => string.Equals(entry.Status, section, StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0)
            {
                continue;
            }

            builder.AppendLine($"[{section}]");
            foreach (var entry in entries)
            {
                builder.AppendLine($"- {entry.DisplayName} | Manager: {entry.Manager} | Package: {entry.PackageId} | {entry.Message}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void ReportCheck(IProgress<MigrationProgressInfo>? progress, string stage, string message, string? currentApp, int processedItems, int totalItems, bool isIndeterminate)
    {
        var fraction = totalItems == 0 ? 0d : (double)Math.Min(processedItems + 1, totalItems) / totalItems;
        var percent = CheckPhaseWeight * fraction;
        ReportOverall(progress, stage, message, currentApp, percent, processedItems + 1, totalItems, isIndeterminate);
    }

    private static void ReportInstall(IProgress<MigrationProgressInfo>? progress, string stage, string message, string? currentApp, int processedItems, int totalItems, bool isIndeterminate)
    {
        var fraction = totalItems == 0 ? 1d : (double)Math.Min(processedItems, totalItems) / totalItems;
        var percent = CheckPhaseWeight + (InstallPhaseWeight * fraction);
        ReportOverall(progress, stage, message, currentApp, percent, processedItems, totalItems, isIndeterminate);
    }

    private static void ReportOverall(IProgress<MigrationProgressInfo>? progress, string stage, string message, string? currentApp, double percent, int processedItems, int totalItems, bool isIndeterminate)
    {
        progress?.Report(new MigrationProgressInfo
        {
            Stage = stage,
            Message = message,
            CurrentApp = currentApp,
            Percent = Math.Min(100d, Math.Max(0d, percent)),
            ProcessedItems = processedItems,
            TotalItems = totalItems,
            IsIndeterminate = isIndeterminate
        });
    }

    private sealed class PackageInstallPlan
    {
        public PackageInstallRequest Request { get; set; } = new();
        public PackageInstallResult? ImmediateResult { get; set; }
        public string? PrimaryManager { get; set; }
        public string? PrimaryPackageId { get; set; }
        public string? SecondaryManager { get; set; }
        public string? SecondaryPackageId { get; set; }
        public bool ShouldInstall => ImmediateResult is null && !string.IsNullOrWhiteSpace(PrimaryManager) && !string.IsNullOrWhiteSpace(PrimaryPackageId);

        public static PackageInstallPlan FromImmediate(PackageInstallRequest request, PackageInstallResult result)
            => new()
            {
                Request = request,
                ImmediateResult = result
            };
    }
}
