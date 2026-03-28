using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppMigrator.UI.Models;

public sealed class DiscoveredApp : INotifyPropertyChanged
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
    public string Category { get; set; } = "Unknown";
    public string RestoreStrategy { get; set; } = "unsupported";
    public string RuleId { get; set; } = string.Empty;
    public string? WingetId { get; set; }
    public bool Supported { get; set; }
    public double Confidence { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string BadgeText => BuildBadge(DisplayName);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string BuildBadge(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "WA";
        }

        var parts = value.Split(' ', '-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
        }

        return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
    }
}
