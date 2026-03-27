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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
