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

                long totalBytes = manifest.Apps.SelectMany(a => a.Paths).Sum(p => p.SizeBytes);
                long processedBytes = 0;
                log?.Report($"Loaded backup with {manifest.Apps.Count} app entries.");

                for (var appIndex = 0; appIndex < manifest.Apps.Count; appIndex++)
                {
                    var app = manifest.Apps[appIndex];
                    log?.Report($"Restoring: {app.DisplayName}");
                    Report(progress, "Restore", $"Preparing restore for {app.DisplayName}", app.DisplayName, appIndex, manifest.Apps.Count, processedBytes, totalBytes);

                    if (attemptWinGetInstall && !string.IsNullOrWhiteSpace(app.WingetId))
                    {
                        if (_winGetService.IsAvailable())
                        {
                            log?.Report($"Attempting WinGet install for {app.DisplayName} ({app.WingetId})");
                            var install = await _winGetService.InstallWithRecoveryAsync(app.WingetId!);
                            log?.Report(install.Succeeded
                                ? $"WinGet install completed for {app.DisplayName}"
                                : $"WinGet install warning for {app.DisplayName}: {install.Message}");
                        }
                        else
                        {
                            log?.Report("WinGet is not available on this system.");
                        }
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
                                log?.Report($"Warning: backup directory missing: {sourcePath}");
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
                                log?.Report($"Warning: backup file missing: {sourcePath}");
                            }
                        }
                    }

                    foreach (var registryEntry in app.Registry.Where(r => r.Succeeded))
                    {
                        var regFile = Path.Combine(extractionRoot, registryEntry.BackupRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(regFile))
                        {
                            log?.Report($"Warning: registry backup file missing: {regFile}");
                            continue;
                        }

                        log?.Report($"Importing registry: {registryEntry.RegistryKeyPath}");
                        var import = await _registryService.ImportKeyAsync(regFile);
                        log?.Report(import.Succeeded
                            ? $"Registry import ok: {registryEntry.RegistryKeyPath}"
                            : $"Registry import warning: {registryEntry.RegistryKeyPath} - {import.Error}");
                    }

                    if (app.Warnings.Count > 0)
                    {
                        log?.Report($"Restore notes for {app.DisplayName}: {string.Join(" | ", app.Warnings)}");
                    }

                    reportLines.Add($"OK | {app.DisplayName} | Paths: {app.Paths.Count} | Registry: {app.Registry.Count} | Warnings: {app.Warnings.Count}");
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
