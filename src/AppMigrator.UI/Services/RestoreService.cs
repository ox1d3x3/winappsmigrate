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
    private readonly KnownRuleRepository _ruleRepository = new();

    public RestoreService(RegistryService registryService, WinGetService winGetService)
    {
        _registryService = registryService;
        _winGetService = winGetService;
    }

    public Task RestoreAsync(string backupZipPath, bool attemptWinGetInstall, IProgress<MigrationProgressInfo>? progress = null, IProgress<string>? log = null)
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
                ZipFile.ExtractToDirectory(backupZipPath, extractionRoot);
                var manifestPath = Path.Combine(extractionRoot, "manifest.json");

                if (!File.Exists(manifestPath))
                {
                    throw new InvalidOperationException("Backup manifest.json is missing.");
                }

                var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath), JsonHelper.DefaultOptions)
                    ?? throw new InvalidOperationException("Backup manifest could not be parsed.");

                var discovery = new AppDiscoveryService(_ruleRepository);
                var installedApps = await discovery.DiscoverAsync();
                long totalBytes = manifest.Apps.SelectMany(a => a.Paths).Sum(p => p.SizeBytes);
                long processedBytes = 0;
                log?.Report($"Loaded backup with {manifest.Apps.Count} app entries.");

                for (var appIndex = 0; appIndex < manifest.Apps.Count; appIndex++)
                {
                    var app = manifest.Apps[appIndex];
                    var perAppWarnings = new List<string>();
                    log?.Report($"Restoring: {app.DisplayName}");
                    Report(progress, "Restore", $"Preparing restore for {app.DisplayName}", app.DisplayName, appIndex, manifest.Apps.Count, processedBytes, totalBytes);

                    if (attemptWinGetInstall)
                    {
                        await EnsureAppInstalledAsync(app, installedApps, perAppWarnings, log);
                    }

                    foreach (var pathEntry in app.Paths)
                    {
                        var sourcePath = Path.Combine(extractionRoot, pathEntry.BackupRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        var targetPath = PathHelper.RemapToCurrentMachine(pathEntry.OriginalPath, manifest.Machine);

                        if (pathEntry.PathType == "directory")
                        {
                            log?.Report($"Restoring directory: {targetPath}");
                            if (Directory.Exists(sourcePath))
                            {
                                FileCopyHelper.CopyDirectory(sourcePath, targetPath, Array.Empty<string>(), log, bytes =>
                                {
                                    processedBytes += bytes;
                                    Report(progress, "Restore", $"Restoring files for {app.DisplayName}", app.DisplayName, appIndex, manifest.Apps.Count, processedBytes, totalBytes);
                                });
                            }
                            else
                            {
                                var warning = $"Backup directory missing: {sourcePath}";
                                perAppWarnings.Add(warning);
                                log?.Report($"Warning: {warning}");
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
                                    Report(progress, "Restore", $"Restoring file for {app.DisplayName}", app.DisplayName, appIndex, manifest.Apps.Count, processedBytes, totalBytes);
                                });
                            }
                            else
                            {
                                var warning = $"Backup file missing: {sourcePath}";
                                perAppWarnings.Add(warning);
                                log?.Report($"Warning: {warning}");
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

                    var status = perAppWarnings.Count == 0 ? "OK" : "WARN";
                    reportLines.Add($"{status} | {app.DisplayName} | Paths: {app.Paths.Count} | Registry: {app.Registry.Count} | Warnings: {perAppWarnings.Count}");
                }

                var reportPath = Path.Combine(extractionRoot, "restore_report.txt");
                await File.WriteAllTextAsync(reportPath, BuildReport(reportLines));
                var sidecar = Path.Combine(Path.GetDirectoryName(backupZipPath)!, $"{Path.GetFileNameWithoutExtension(backupZipPath)}.restore_report.txt");
                File.Copy(reportPath, sidecar, true);

                Report(progress, "Restore", "Restore completed.", null, manifest.Apps.Count, manifest.Apps.Count, totalBytes, totalBytes);
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

    private async Task EnsureAppInstalledAsync(AppBackupEntry app, List<DiscoveredApp> installedApps, List<string> warnings, IProgress<string>? log)
    {
        if (_winGetService.IsAvailable() is false)
        {
            log?.Report("WinGet is not available on this system.");
            return;
        }

        if (IsAlreadyInstalled(app, installedApps))
        {
            log?.Report($"Install check: {app.DisplayName} already appears to be installed. Skipping WinGet install.");
            return;
        }

        string? packageId = app.WingetId;
        if (string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(app.AppId))
        {
            packageId = _ruleRepository.GetById(app.AppId)?.WingetId;
        }

        if (string.IsNullOrWhiteSpace(packageId))
        {
            log?.Report($"Searching WinGet for {app.DisplayName}...");
            var lookup = await _winGetService.FindPackageIdByNameAsync(app.DisplayName);
            if (lookup.Found)
            {
                packageId = lookup.PackageId;
                log?.Report(lookup.Message);
            }
            else
            {
                var warning = $"WinGet package was not found for {app.DisplayName}.";
                warnings.Add(warning);
                log?.Report($"Warning: {warning} {lookup.Message}".Trim());
                return;
            }
        }

        log?.Report($"Attempting WinGet install for {app.DisplayName} ({packageId})");
        var install = await _winGetService.InstallWithRecoveryAsync(packageId!);
        if (install.Succeeded)
        {
            log?.Report($"WinGet install completed for {app.DisplayName}");
            await Task.Delay(TimeSpan.FromSeconds(3));
            installedApps.Clear();
            installedApps.AddRange(await new AppDiscoveryService(_ruleRepository).DiscoverAsync());
            return;
        }

        var warningText = $"WinGet install warning for {app.DisplayName}: {install.Message}";
        warnings.Add(warningText);
        log?.Report(warningText);
    }

    private static bool IsAlreadyInstalled(AppBackupEntry app, IEnumerable<DiscoveredApp> installedApps)
    {
        foreach (var installed in installedApps)
        {
            if (!string.IsNullOrWhiteSpace(app.WingetId)
                && !string.IsNullOrWhiteSpace(installed.WingetId)
                && string.Equals(app.WingetId, installed.WingetId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(app.AppId)
                && string.Equals(installed.RuleId, app.AppId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(installed.DisplayName, app.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (installed.DisplayName.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase)
                || app.DisplayName.Contains(installed.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Report(IProgress<MigrationProgressInfo>? progress, string stage, string message, string? currentApp, int currentIndex, int totalApps, long processedBytes, long totalBytes)
    {
        var appWeight = totalApps == 0 ? 0 : (double)currentIndex / totalApps * 100d;
        var byteWeight = totalBytes <= 0 ? 0 : (double)processedBytes / totalBytes * 100d;
        var percent = Math.Min(100d, Math.Max(appWeight, byteWeight));

        progress?.Report(new MigrationProgressInfo
        {
            Stage = stage,
            Message = message,
            CurrentApp = currentApp,
            Percent = percent,
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes
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
