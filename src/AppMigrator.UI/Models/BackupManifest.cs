using System;
using System.Collections.Generic;

namespace AppMigrator.UI.Models;

public sealed class BackupManifest
{
    public string ToolName { get; set; } = AppMigrator.UI.AppMetadata.ProductName;
    public string ToolVersion { get; set; } = AppMigrator.UI.AppMetadata.Version;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public MachineProfile Machine { get; set; } = new();
    public List<AppBackupEntry> Apps { get; set; } = new();
}

public sealed class MachineProfile
{
    public string MachineName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public string OSVersion { get; set; } = Environment.OSVersion.ToString();
    public string Framework { get; set; } = Environment.Version.ToString();
    public string UserProfilePath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string LocalAppDataPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string RoamingAppDataPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
}

public sealed class AppBackupEntry
{
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RestoreStrategy { get; set; } = string.Empty;
    public string? WingetId { get; set; }
    public string OriginalInstallLocation { get; set; } = string.Empty;
    public List<string> ProcessNames { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<BackupPathEntry> Paths { get; set; } = new();
    public List<RegistryBackupEntry> Registry { get; set; } = new();
}

public sealed class BackupPathEntry
{
    public string OriginalPath { get; set; } = string.Empty;
    public string BackupRelativePath { get; set; } = string.Empty;
    public string PathType { get; set; } = "directory";
    public long SizeBytes { get; set; }
}

public sealed class RegistryBackupEntry
{
    public string RegistryKeyPath { get; set; } = string.Empty;
    public string BackupRelativePath { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
}
