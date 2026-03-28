using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

    public async Task RestoreAsync(string backupZipPath, bool attemptWinGetInstall, IProgress<string>? progress = null)
    {
        if (!File.Exists(backupZipPath))
        {
            throw new FileNotFoundException("Backup ZIP not found.", backupZipPath);
        }

        var extractionRoot = Path.Combine(Path.GetTempPath(), $"WinAppsMigratorRestore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractionRoot);

        try
        {
            ZipFile.ExtractToDirectory(backupZipPath, extractionRoot);
            var manifestPath = Path.Combine(extractionRoot, "manifest.json");

            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Backup manifest.json is missing.");
            }

            var manifest = JsonSerializer.Deserialize<BackupManifest>(
                await File.ReadAllTextAsync(manifestPath),
                JsonHelper.DefaultOptions);

            if (manifest is null)
            {
                throw new InvalidOperationException("Backup manifest could not be parsed.");
            }

            progress?.Report($"Loaded backup with {manifest.Apps.Count} app entries.");

            foreach (var app in manifest.Apps)
            {
                progress?.Report($"Restoring: {app.DisplayName}");

                if (attemptWinGetInstall && !string.IsNullOrWhiteSpace(app.WingetId))
                {
                    if (_winGetService.IsAvailable())
                    {
                        progress?.Report($"Attempting WinGet install for {app.DisplayName} ({app.WingetId})");
                        var install = await _winGetService.InstallAsync(app.WingetId!);
                        progress?.Report(install.Succeeded
                            ? $"WinGet install completed for {app.DisplayName}"
                            : $"WinGet install warning for {app.DisplayName}: {install.Message}");
                    }
                    else
                    {
                        progress?.Report("WinGet is not available on this system.");
                    }
                }

                foreach (var pathEntry in app.Paths)
                {
                    var sourcePath = Path.Combine(extractionRoot, pathEntry.BackupRelativePath.Replace('/', Path.DirectorySeparatorChar));

                    if (pathEntry.PathType == "directory")
                    {
                        progress?.Report($"Restoring directory: {pathEntry.OriginalPath}");
                        if (Directory.Exists(sourcePath))
                        {
                            FileCopyHelper.CopyDirectory(sourcePath, pathEntry.OriginalPath, Array.Empty<string>(), progress);
                        }
                        else
                        {
                            progress?.Report($"Warning: backup directory missing: {sourcePath}");
                        }
                    }
                    else
                    {
                        progress?.Report($"Restoring file: {pathEntry.OriginalPath}");
                        if (File.Exists(sourcePath))
                        {
                            FileCopyHelper.CopyFile(sourcePath, pathEntry.OriginalPath);
                        }
                        else
                        {
                            progress?.Report($"Warning: backup file missing: {sourcePath}");
                        }
                    }
                }

                foreach (var registryEntry in app.Registry.Where(r => r.Succeeded))
                {
                    var regFile = Path.Combine(extractionRoot, registryEntry.BackupRelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(regFile))
                    {
                        progress?.Report($"Warning: registry backup file missing: {regFile}");
                        continue;
                    }

                    progress?.Report($"Importing registry: {registryEntry.RegistryKeyPath}");
                    var import = await _registryService.ImportKeyAsync(regFile);
                    progress?.Report(import.Succeeded
                        ? $"Registry import ok: {registryEntry.RegistryKeyPath}"
                        : $"Registry import warning: {registryEntry.RegistryKeyPath} - {import.Error}");
                }

                if (app.Warnings.Count > 0)
                {
                    progress?.Report($"Restore notes for {app.DisplayName}: {string.Join(" | ", app.Warnings)}");
                }
            }

            progress?.Report("Restore finished.");
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
                // Best effort cleanup
            }
        }
    }
}
