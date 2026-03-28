namespace AppMigrator.UI.Models;

public sealed class MigrationProgressInfo
{
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? CurrentApp { get; set; }
    public double Percent { get; set; }
    public long ProcessedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int ProcessedItems { get; set; }
    public int TotalItems { get; set; }
    public bool IsIndeterminate { get; set; }
}
