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

public sealed class BackupService
{
    private readonly KnownRuleRepository _ruleRepository;
    private readonly RegistryService _registryService;

    public BackupService(KnownRuleRepository ruleRepository, RegistryService registryService)
    {
        _ruleRepository = ruleRepository;
        _registryService = registryService;
    }

    public Task<string> CreateBackupAsync(IReadOnlyList<DiscoveredApp> apps, string outputZipPath, IProgress<MigrationProgressInfo>? progress = null, IProgress<string>? log = null)
        => Task.Run(async () =>
        {
            if (apps.Count == 0)
            {
                throw new InvalidOperationException("No apps were selected for backup.");
            }

            var stagingRoot = Path.Combine(Path.GetTempPath(), $"WinAppsMigrator_{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingRoot);
            var reportLines = new List<string>();
            long totalBytes = EstimateTotalBytes(apps);
            long processedBytes = 0;

            try
            {
                var manifest = new BackupManifest();
                log?.Report($"Preparing backup for {apps.Count} app(s).");

                for (var index = 0; index < apps.Count; index++)
                {
                    var app = apps[index];
                    var rule = _ruleRepository.GetById(app.RuleId);
                    var appRoot = Path.Combine(stagingRoot, "apps", PathHelper.MakeSafeName(app.RuleId == string.Empty ? app.DisplayName : app.RuleId));
                    Directory.CreateDirectory(appRoot);

                    var entry = new AppBackupEntry
                    {
                        AppId = string.IsNullOrWhiteSpace(app.RuleId) ? app.DisplayName : app.RuleId,
                        DisplayName = app.DisplayName,
                        Version = app.Version,
                        Category = app.Category,
                        RestoreStrategy = app.RestoreStrategy,
                        WingetId = app.WingetId,
                        OriginalInstallLocation = app.InstallLocation,
                        Notes = rule?.Notes?.ToList() ?? new List<string> { app.Notes },
                        ProcessNames = rule?.ProcessNames?.ToList() ?? new List<string>()
                    };

                    Report(progress, "Backup", $"Preparing backup for {app.DisplayName}", app.DisplayName, index, apps.Count, processedBytes, totalBytes);
                    log?.Report($"Preparing backup for {app.DisplayName}");

                    if (rule is null || !rule.Supported)
                    {
                        entry.Warnings.Add("This app has no built-in backup rule yet. Metadata only was captured.");
                        manifest.Apps.Add(entry);
                        reportLines.Add($"SKIPPED | {app.DisplayName} | No built-in rule");
                        continue;
                    }

                    var candidatePaths = new List<string>();
                    candidatePaths.AddRange(rule.IncludePaths.Select(PathHelper.ExpandVariables));

                    if (rule.IncludeInstallLocation && !string.IsNullOrWhiteSpace(app.InstallLocation))
                    {
                        candidatePaths.Add(app.InstallLocation);
                    }

                    var pathIndex = 1;
                    foreach (var candidatePath in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(candidatePath))
                        {
                            continue;
                        }

                        if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
                        {
                            entry.Warnings.Add($"Path not found: {candidatePath}");
                            continue;
                        }

                        var pathType = Directory.Exists(candidatePath) ? "directory" : "file";
                        var verificationEntries = FileCopyHelper.BuildVerificationEntries(candidatePath, rule.ExcludeGlobs);
                        var backupRelative = Path.Combine("apps", PathHelper.MakeSafeName(entry.AppId), "files", $"item_{pathIndex:D2}");
                        var backupAbsolute = Path.Combine(stagingRoot, backupRelative);
                        log?.Report($"Capturing path: {candidatePath}");

                        long copied;
                        if (string.Equals(pathType, "directory", StringComparison.OrdinalIgnoreCase))
                        {
                            var size = FileCopyHelper.GetPathSize(candidatePath, rule.ExcludeGlobs);
                            copied = FileCopyHelper.CopyDirectory(candidatePath, backupAbsolute, rule.ExcludeGlobs, log, bytes =>
                            {
                                processedBytes += bytes;
                                Report(progress, "Backup", $"Copying files for {app.DisplayName}", app.DisplayName, index, apps.Count, processedBytes, totalBytes);
                            });

                            var verification = FileCopyHelper.VerifyPath(backupAbsolute, pathType, verificationEntries);
                            if (!verification.Succeeded)
                            {
                                entry.Warnings.AddRange(verification.Warnings);
                                log?.Report($"Verification warning for {candidatePath}: {string.Join(" | ", verification.Warnings)}");
                            }

                            entry.Paths.Add(new BackupPathEntry
                            {
                                OriginalPath = candidatePath,
                                BackupRelativePath = backupRelative.Replace('\\', '/'),
                                PathType = pathType,
                                SizeBytes = size == 0 ? copied : size,
                                FileCount = verificationEntries.Count,
                                BackupVerified = verification.Succeeded,
                                Files = verificationEntries
                            });
                        }
                        else
                        {
                            copied = FileCopyHelper.CopyFile(candidatePath, backupAbsolute, bytes =>
                            {
                                processedBytes += bytes;
                                Report(progress, "Backup", $"Copying file for {app.DisplayName}", app.DisplayName, index, apps.Count, processedBytes, totalBytes);
                            });

                            var verification = FileCopyHelper.VerifyPath(backupAbsolute, pathType, verificationEntries);
                            if (!verification.Succeeded)
                            {
                                entry.Warnings.AddRange(verification.Warnings);
                                log?.Report($"Verification warning for {candidatePath}: {string.Join(" | ", verification.Warnings)}");
                            }

                            entry.Paths.Add(new BackupPathEntry
                            {
                                OriginalPath = candidatePath,
                                BackupRelativePath = backupRelative.Replace('\\', '/'),
                                PathType = pathType,
                                SizeBytes = copied,
                                FileCount = verificationEntries.Count,
                                BackupVerified = verification.Succeeded,
                                Files = verificationEntries
                            });
                        }

                        pathIndex++;
                    }

                    var registryIndex = 1;
                    foreach (var registryKey in rule.RegistryKeys.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var relative = Path.Combine("apps", PathHelper.MakeSafeName(entry.AppId), "registry", $"key_{registryIndex:D2}.reg");
                        var absolute = Path.Combine(stagingRoot, relative);
                        log?.Report($"Exporting registry: {registryKey}");
                        var export = await _registryService.ExportKeyAsync(registryKey, absolute);

                        entry.Registry.Add(new RegistryBackupEntry
                        {
                            RegistryKeyPath = registryKey,
                            BackupRelativePath = relative.Replace('\\', '/'),
                            Succeeded = export.Succeeded,
                            Error = export.Error
                        });

                        if (!export.Succeeded)
                        {
                            entry.Warnings.Add($"Registry export failed for {registryKey}: {export.Error}");
                        }

                        registryIndex++;
                    }

                    var perAppManifestPath = Path.Combine(appRoot, "app_manifest.json");
                    await File.WriteAllTextAsync(perAppManifestPath, JsonSerializer.Serialize(entry, JsonHelper.DefaultOptions));
                    manifest.Apps.Add(entry);
                    var verifiedCount = entry.Paths.Count(path => path.BackupVerified);
                    reportLines.Add($"OK | {app.DisplayName} | Paths: {entry.Paths.Count} | Verified: {verifiedCount}/{entry.Paths.Count} | Registry: {entry.Registry.Count} | Warnings: {entry.Warnings.Count}");
                }

                var manifestPath = Path.Combine(stagingRoot, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonHelper.DefaultOptions));

                var reportPath = Path.Combine(stagingRoot, "backup_report.txt");
                await File.WriteAllTextAsync(reportPath, BuildReport("Backup", reportLines));

                if (File.Exists(outputZipPath))
                {
                    File.Delete(outputZipPath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
                ZipFile.CreateFromDirectory(stagingRoot, outputZipPath, CompressionLevel.Optimal, false);

                var sidecarReportPath = Path.ChangeExtension(outputZipPath, ".backup_report.txt");
                File.Copy(reportPath, sidecarReportPath, true);

                log?.Report($"Backup ZIP created: {outputZipPath}");
                Report(progress, "Backup", "Backup completed.", null, apps.Count, apps.Count, totalBytes, totalBytes);
                return outputZipPath;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingRoot))
                    {
                        Directory.Delete(stagingRoot, true);
                    }
                }
                catch
                {
                }
            }
        });

    private long EstimateTotalBytes(IReadOnlyList<DiscoveredApp> apps)
    {
        long total = 0;
        foreach (var app in apps)
        {
            var rule = _ruleRepository.GetById(app.RuleId);
            if (rule is null)
            {
                continue;
            }

            foreach (var path in rule.IncludePaths.Select(PathHelper.ExpandVariables))
            {
                total += FileCopyHelper.GetPathSize(path, rule.ExcludeGlobs);
            }
        }

        return Math.Max(total, 1);
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

    private static string BuildReport(string title, IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{title} report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Tool: {AppMetadata.DisplayTitle}");
        builder.AppendLine();
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }
}
