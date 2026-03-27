using System.IO.Compression;
using System.Text.Json;
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

    public async Task<string> CreateBackupAsync(IEnumerable<DiscoveredApp> selectedApps, string outputZipPath, IProgress<string>? progress = null)
    {
        var apps = selectedApps.ToList();
        if (apps.Count == 0)
        {
            throw new InvalidOperationException("No apps selected.");
        }

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"AppMigrator_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var manifest = new BackupManifest();

            foreach (var app in apps)
            {
                progress?.Report($"Preparing backup for {app.DisplayName}");

                var entry = new AppBackupEntry
                {
                    AppId = string.IsNullOrWhiteSpace(app.RuleId) ? PathHelper.MakeSafeName(app.DisplayName) : app.RuleId,
                    DisplayName = app.DisplayName,
                    Version = app.Version,
                    Category = app.Category,
                    RestoreStrategy = app.RestoreStrategy,
                    WingetId = app.WingetId,
                    OriginalInstallLocation = app.InstallLocation
                };

                if (!app.Supported)
                {
                    entry.Warnings.Add("This app has no built-in backup rule yet. Metadata only was captured.");
                    manifest.Apps.Add(entry);
                    continue;
                }

                var rule = _ruleRepository.GetById(app.RuleId);
                if (rule is null)
                {
                    entry.Warnings.Add($"Rule {app.RuleId} could not be loaded.");
                    manifest.Apps.Add(entry);
                    continue;
                }

                entry.Notes.AddRange(rule.Notes);

                var appRoot = Path.Combine(stagingRoot, "apps", PathHelper.MakeSafeName(entry.AppId));
                var fileRoot = Path.Combine(appRoot, "files");
                var registryRoot = Path.Combine(appRoot, "registry");
                Directory.CreateDirectory(fileRoot);
                Directory.CreateDirectory(registryRoot);

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

                    var backupRelative = Path.Combine("apps", PathHelper.MakeSafeName(entry.AppId), "files", $"item_{pathIndex:D2}");
                    var backupAbsolute = Path.Combine(stagingRoot, backupRelative);

                    progress?.Report($"Capturing path: {candidatePath}");

                    if (Directory.Exists(candidatePath))
                    {
                        FileCopyHelper.CopyDirectory(candidatePath, backupAbsolute, rule.ExcludeGlobs, progress);
                        entry.Paths.Add(new BackupPathEntry
                        {
                            OriginalPath = candidatePath,
                            BackupRelativePath = backupRelative.Replace('\\', '/'),
                            PathType = "directory"
                        });
                    }
                    else
                    {
                        FileCopyHelper.CopyFile(candidatePath, backupAbsolute);
                        entry.Paths.Add(new BackupPathEntry
                        {
                            OriginalPath = candidatePath,
                            BackupRelativePath = backupRelative.Replace('\\', '/'),
                            PathType = "file"
                        });
                    }

                    pathIndex++;
                }

                var registryIndex = 1;
                foreach (var registryKey in rule.RegistryKeys.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var relative = Path.Combine("apps", PathHelper.MakeSafeName(entry.AppId), "registry", $"key_{registryIndex:D2}.reg");
                    var absolute = Path.Combine(stagingRoot, relative);
                    progress?.Report($"Exporting registry: {registryKey}");
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
            }

            var manifestPath = Path.Combine(stagingRoot, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonHelper.DefaultOptions));

            if (File.Exists(outputZipPath))
            {
                File.Delete(outputZipPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
            ZipFile.CreateFromDirectory(stagingRoot, outputZipPath, CompressionLevel.Optimal, false);

            progress?.Report($"Backup ZIP created: {outputZipPath}");
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
                // Best effort cleanup
            }
        }
    }
}
