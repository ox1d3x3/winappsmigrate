using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace AppMigrator.UI.Models;

[XmlRoot("WinAppsMigratorPackageExport")]
public sealed class PackageExportManifest
{
    [XmlAttribute("toolName")]
    public string ToolName { get; set; } = AppMigrator.UI.AppMetadata.ProductName;

    [XmlAttribute("toolVersion")]
    public string ToolVersion { get; set; } = AppMigrator.UI.AppMetadata.Version;

    [XmlAttribute("createdUtc")]
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");

    public PackageExportMachine Machine { get; set; } = new();

    [XmlArray("Packages")]
    [XmlArrayItem("Package")]
    public List<PackageExportEntry> Packages { get; set; } = new();
}

public sealed class PackageExportMachine
{
    public string MachineName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public string OSVersion { get; set; } = Environment.OSVersion.ToString();
}

public sealed class PackageExportEntry
{
    [XmlAttribute("appId")]
    public string AppId { get; set; } = string.Empty;

    [XmlAttribute("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [XmlAttribute("publisher")]
    public string Publisher { get; set; } = string.Empty;

    [XmlAttribute("version")]
    public string Version { get; set; } = string.Empty;

    [XmlAttribute("restoreStrategy")]
    public string RestoreStrategy { get; set; } = string.Empty;

    [XmlAttribute("supported")]
    public bool Supported { get; set; }

    [XmlAttribute("wingetId")]
    public string WingetId { get; set; } = string.Empty;

    [XmlAttribute("chocolateyId")]
    public string ChocolateyId { get; set; } = string.Empty;
}

public sealed class PackageInstallRequest
{
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string RestoreStrategy { get; set; } = string.Empty;
    public bool Supported { get; set; }
    public string? WingetId { get; set; }
    public string? ChocolateyId { get; set; }
}

public sealed class PackageInstallResult
{
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Manager { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class PackageRestoreExecutionResult
{
    public List<PackageInstallResult> Results { get; set; } = new();

    public int AlreadyInstalledCount => Results.Count(x => string.Equals(x.Status, "AlreadyInstalled", StringComparison.OrdinalIgnoreCase));
    public int InstalledCount => Results.Count(x => string.Equals(x.Status, "Installed", StringComparison.OrdinalIgnoreCase));
    public int NotFoundCount => Results.Count(x => string.Equals(x.Status, "NotFound", StringComparison.OrdinalIgnoreCase));
    public int SkippedCount => Results.Count(x => string.Equals(x.Status, "Skipped", StringComparison.OrdinalIgnoreCase));
    public int FailedCount => Results.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase));
    public int WarningCount => Results.Count(x => !string.Equals(x.Status, "Installed", StringComparison.OrdinalIgnoreCase)
                                               && !string.Equals(x.Status, "AlreadyInstalled", StringComparison.OrdinalIgnoreCase));
    public int TotalCount => Results.Count;
}
