using System.Collections.Generic;

namespace AppMigrator.UI.Models;

public sealed class AppRule
{
    public string Id { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string Category { get; set; } = "Unknown";
    public string RestoreStrategy { get; set; } = "unsupported";
    public bool Supported { get; set; }
    public double Confidence { get; set; }
    public string? WingetId { get; set; }
    public bool IncludeInstallLocation { get; set; }
    public List<string> MatchTokens { get; set; } = new();
    public List<string> IncludePaths { get; set; } = new();
    public List<string> RegistryKeys { get; set; } = new();
    public List<string> ExcludeGlobs { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}
