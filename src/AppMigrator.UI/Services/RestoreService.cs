using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AppMigrator.UI.Helpers;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Services;

public sealed class RestoreService
{
    private readonly RegistryService _registryService;
    private readonly WinGetService _winGetService;
    private readonly ChocolateyService _chocolateyService;
    private readonly KnownRuleRepository _ruleRepository = new();

    public RestoreService(RegistryService registryService, WinGetService winGetService, ChocolateyService chocolateyService)
    {
        _registryService = registryService;
        _winGetService = winGetService;
        _chocolateyService = chocolateyService;
    }

    public Task RestoreAsync(string backupZipPath, bool attemptPackageInstall, IProgress<MigrationProgressInfo>? progress = null, IProgress<string>? log = null)
        => Task.Run(async () =>
        {
            if (!File.Exists(backupZipPath))
            {
                throw new FileNotFoundException("Backup ZIP not found.", backupZipPath);
            }

            var extractionRoot = Path.Combine(Path.GetTempPath(), $"WinAppsMigratorRestore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractionRoot);
            var reportLines = new List<string>();

            try
            {
                progress?.Report(new MigrationProgressInfo
                {
                    Stage = "Restore",
                    Message = "Extracting backup ZIP",
                    Percent = 0,
                    TotalBytes = 1,
                    IsIndeterminate = true
                });

                ZipFile.ExtractToDirectory(backupZipPath, extractionRoot);
                var manifestPath = Path.Combine(extractionRoot, "manifest.json");

                if (!File.Exists(manifestPath))
                {
                    throw new InvalidOperationException("Backup manifest.json is missing.");
                }

                var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath), JsonHelper.DefaultOptions)
                    ?? throw new InvalidOperationException("Backup manifest could not be parsed.");

                long totalBytes = manifest.Apps.SelectMany(a => a.Paths).Sum(p => p.SizeBytes);
                long processedBytes = 0;
                var packageProgressBase = attemptPackageInstall ? 35d : 0d;
                var restoreProgressWeight = 100d - packageProgressBase;
                log?.Report($"Loaded backup with {manifest.Apps.Count} app entries.");

                if (attemptPackageInstall)
                {
                    var packageRequests = manifest.Apps
                        .Select(app => new PackageInstallRequest
                        {
                            AppId = app.AppId,
                            DisplayName = app.DisplayName,
                            Version = app.Version,
                            RestoreStrategy = app.RestoreStrategy,
                            Supported = !string.Equals(app.RestoreStrategy, "unsupported", StringComparison.OrdinalIgnoreCase),
                            WingetId = app.WingetId,
                            ChocolateyId = app.ChocolateyId
                        })
                        .Where(app => !string.IsNullOrWhiteSpace(app.DisplayName))
                        .ToList();

                    if (packageRequests.Count > 0)
                    {
                        var packageRestoreService = new PackageRestoreService(_ruleRepository, _winGetService, _chocolateyService);
                        var packageResult = await packageRestoreService.RestorePackagesAsync(packageRequests, new Progress<MigrationProgressInfo>(pkg =>
                        {
                            progress?.Report(new MigrationProgressInfo
                            {
                                Stage = pkg.Stage,
                                Message = pkg.Message,
                                CurrentApp = pkg.CurrentApp,
                                Percent = Math.Min(packageProgressBase, pkg.Percent * (packageProgressBase / 100d)),
                                ProcessedItems = pkg.ProcessedItems,
                                TotalItems = pkg.TotalItems,
                                IsIndeterminate = pkg.IsIndeterminate,
                                ProcessedBytes = 0,
                                TotalBytes = 0
                            });
                        }), log);

                        foreach (var packageLine in packageResult.Results.Where(x => string.Equals(x.Status, "Warning", StringComparison.OrdinalIgnoreCase)))
                        {
                            reportLines.Add($"WARN | {packageLine.DisplayName} | Package manager: {packageLine.Manager} | {packageLine.Message}");
                        }
                    }
                }
                else
                {
                    log?.Report("Package reinstall step skipped by user choice.");
                }

                for (var appIndex = 0; appIndex < manifest.Apps.Count; appIndex++)
                {
                    var app = manifest.Apps[appIndex];
                    var perAppWarnings = new List<string>();
                    log?.Report($"Restoring: {app.DisplayName}");
                    Report(progress, "Restore", $"Preparing restore for {app.DisplayName}", app.DisplayName, appIndex, manifest.Apps.Count, processedBytes, totalBytes, packageProgressBase, restoreProgressWeight);

                    foreach (var pathEntry in app.Paths)
                    {
                        var sourcePath = Path.Combine(extractionRoot, pathEntry.BackupRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        var targetPath = PathHelper.RemapToCurrentMachine(pathEntry.OriginalPath, manifest.Machine);

                        if (string.Equals(pathEntry.PathType, "directory", StringComparison.OrdinalIgnoreCase))
                        {
                            log?.Report($"Restoring directory: {targetPath}");
                            if (Directory.Exists(sourcePath))
                            {
                                FileCopyHelper.CopyDirectory(sourcePath, targetPath, Array.Empty<string>(), log, bytes =>
                                {
                                    processedBytes += bytes;
                                    Report(progress, "Restore", $"Restoring files for {app.DisplayName}", app.DisplayName, appIndex, manifest.Apps.Count, processedBytes, totalBytes, packageProgressBase, restoreProgressWeight);
                                });
                            }
                            else
                            {
                                var warning = $"Backup directory missing: {sourcePath}";
                                perAppWarnings.Add(warning);
                                log?.Report($"Warning: {warning}");
                                continue;
                            }
                        }
                        else
                        {
                            log?.Report($"Restoring file: {targetPath}");
                            if (File.Exists(sourcePath))
                            {
                                FileCopyHelper.CopyFile(sourcePath, targetPath, bytes =>
                                {
                                    processedBytes += bytes;
                                    Report(progress, "Restore", $"Restoring file for {app.DisplayName}", app.DisplayName, appIndex, manifest.Apps.Count, processedBytes, totalBytes, packageProgressBase, restoreProgressWeight);
                                });
                            }
                            else
                            {
                                var warning = $"Backup file missing: {sourcePath}";
                                perAppWarnings.Add(warning);
                                log?.Report($"Warning: {warning}");
                                continue;
                            }
                        }

                        if (pathEntry.Files.Count > 0)
                        {
                            var verification = FileCopyHelper.VerifyPath(targetPath, pathEntry.PathType, pathEntry.Files);
                            pathEntry.RestoreVerified = verification.Succeeded;
                            if (!verification.Succeeded)
                            {
                                perAppWarnings.AddRange(verification.Warnings);
                                log?.Report($"Verification warning for {app.DisplayName}: {string.Join(" | ", verification.Warnings)}");
                            }
                            else
                            {
                                log?.Report($"Verification passed for {app.DisplayName}: {pathEntry.FileCount} file(s) verified.");
                            }
                        }
                    }

                    foreach (var registryEntry in app.Registry.Where(r => r.Succeeded))
                    {
                        var regFile = Path.Combine(extractionRoot, registryEntry.BackupRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(regFile))
                        {
                            var warning = $"Registry backup file missing: {regFile}";
                            perAppWarnings.Add(warning);
                            log?.Report($"Warning: {warning}");
                            continue;
                        }

                        log?.Report($"Importing registry: {registryEntry.RegistryKeyPath}");
                        var import = await _registryService.ImportKeyAsync(regFile);
                        if (import.Succeeded)
                        {
                            log?.Report($"Registry import ok: {registryEntry.RegistryKeyPath}");
                        }
                        else
                        {
                            var warning = $"Registry import warning: {registryEntry.RegistryKeyPath} - {import.Error}";
                            perAppWarnings.Add(warning);
                            log?.Report(warning);
                        }
                    }

                    if (app.Warnings.Count > 0)
                    {
                        perAppWarnings.AddRange(app.Warnings);
                        log?.Report($"Restore notes for {app.DisplayName}: {string.Join(" | ", app.Warnings)}");
                    }

                    var verifiedCount = app.Paths.Count(path => path.RestoreVerified || path.Files.Count == 0);
                    var status = perAppWarnings.Count == 0 ? "OK" : "WARN";
                    reportLines.Add($"{status} | {app.DisplayName} | Paths: {app.Paths.Count} | Verified: {verifiedCount}/{app.Paths.Count} | Registry: {app.Registry.Count} | Warnings: {perAppWarnings.Count}");
                }

                var reportPath = Path.Combine(extractionRoot, "restore_report.txt");
                await File.WriteAllTextAsync(reportPath, BuildReport(reportLines));
                var sidecar = Path.Combine(Path.GetDirectoryName(backupZipPath)!, $"{Path.GetFileNameWithoutExtension(backupZipPath)}.restore_report.txt");
                File.Copy(reportPath, sidecar, true);

                Report(progress, "Restore", "Restore completed.", null, manifest.Apps.Count, manifest.Apps.Count, totalBytes, totalBytes, packageProgressBase, restoreProgressWeight);
                log?.Report("Restore finished.");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(extractionRoot))
                    {
                        Directory.Delete(extractionRoot, true);
                    }
                }
                catch
                {
                }
            }
        });

    private static void Report(IProgress<MigrationProgressInfo>? progress, string stage, string message, string? currentApp, int currentIndex, int totalApps, long processedBytes, long totalBytes, double basePercent, double activeWeight)
    {
        var bytesPortion = totalBytes <= 0 ? 0 : (double)processedBytes / Math.Max(totalBytes, 1) * activeWeight;
        var appsPortion = totalApps == 0 ? 0 : (double)currentIndex / totalApps * activeWeight;
        var percent = totalBytes <= 0
            ? Math.Min(100d, totalApps == 0 ? basePercent : basePercent + ((double)currentIndex / totalApps * activeWeight))
            : Math.Min(100d, basePercent + Math.Max(bytesPortion, appsPortion));

        progress?.Report(new MigrationProgressInfo
        {
            Stage = stage,
            Message = message,
            CurrentApp = currentApp,
            Percent = percent,
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes,
            ProcessedItems = currentIndex,
            TotalItems = totalApps,
            IsIndeterminate = false
        });
    }

    private static string BuildReport(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Restore report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Tool: {AppMetadata.DisplayTitle}");
        builder.AppendLine();
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }
}
