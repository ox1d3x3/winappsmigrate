using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Services;

public sealed class PackageManifestService
{
    public async Task ExportAsync(string outputPath, IReadOnlyList<DiscoveredApp> apps, IProgress<string>? log = null)
    {
        if (apps.Count == 0)
        {
            throw new InvalidOperationException("No apps are available to export.");
        }

        var manifest = new PackageExportManifest
        {
            Packages = apps
                .Where(app => !string.IsNullOrWhiteSpace(app.DisplayName))
                .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(app => new PackageExportEntry
                {
                    AppId = app.RuleId,
                    DisplayName = app.DisplayName,
                    Publisher = app.Publisher,
                    Version = app.Version,
                    RestoreStrategy = app.RestoreStrategy,
                    Supported = app.Supported,
                    WingetId = app.WingetId ?? string.Empty,
                    ChocolateyId = app.ChocolateyId ?? string.Empty
                })
                .ToList()
        };

        await using var stream = File.Create(outputPath);
        var serializer = new XmlSerializer(typeof(PackageExportManifest));
        serializer.Serialize(stream, manifest);
        log?.Report($"Package list exported: {outputPath}");
    }

    public async Task<PackageExportManifest> ImportAsync(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Package XML was not found.", inputPath);
        }

        await using var stream = File.OpenRead(inputPath);
        var serializer = new XmlSerializer(typeof(PackageExportManifest));
        var manifest = serializer.Deserialize(stream) as PackageExportManifest;
        if (manifest is null)
        {
            throw new InvalidOperationException("Package XML could not be parsed.");
        }

        manifest.Packages ??= new List<PackageExportEntry>();
        return manifest;
    }
}
