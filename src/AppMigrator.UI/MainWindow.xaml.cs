using System.Collections.ObjectModel;
using System.Windows;
using AppMigrator.UI.Models;
using AppMigrator.UI.Services;
using Microsoft.Win32;

namespace AppMigrator.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DiscoveredApp> _apps = new();
    private readonly KnownRuleRepository _ruleRepository = new();
    private readonly RegistryService _registryService = new();
    private readonly WinGetService _winGetService = new();

    public MainWindow()
    {
        InitializeComponent();
        AppsDataGrid.ItemsSource = _apps;
        UpdateSummary();
        Log("Ready.");
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusyState(true);
            _apps.Clear();
            Log("Scanning installed applications...");

            var discoveryService = new AppDiscoveryService(_ruleRepository);
            var progress = new Progress<string>(Log);
            var apps = await discoveryService.DiscoverAsync(progress);

            foreach (var app in apps)
            {
                app.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(DiscoveredApp.IsSelected))
                    {
                        UpdateSummary();
                    }
                };

                _apps.Add(app);
            }

            Log($"Scan complete. Found {_apps.Count} apps.");
            UpdateSummary();
        }
        catch (Exception ex)
        {
            Log($"Scan failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SelectSupportedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _apps)
        {
            app.IsSelected = app.Supported;
        }

        UpdateSummary();
        Log("Selected all supported apps.");
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _apps)
        {
            app.IsSelected = false;
        }

        UpdateSummary();
        Log("Selection cleared.");
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _apps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one app first.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "ZIP archives (*.zip)|*.zip",
            Title = "Save backup ZIP",
            FileName = $"AppMigrator_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetBusyState(true);
            Log($"Starting backup for {selected.Count} app(s).");

            var backupService = new BackupService(_ruleRepository, _registryService);
            var progress = new Progress<string>(Log);
            var resultPath = await backupService.CreateBackupAsync(selected, dialog.FileName, progress);

            Log($"Backup completed successfully: {resultPath}");
            MessageBox.Show(this, $"Backup completed successfully:\n{resultPath}", "Backup complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Backup failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ZIP archives (*.zip)|*.zip",
            Title = "Select backup ZIP"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var restoreMessage = UseWingetCheckBox.IsChecked == true
            ? "Restore from the selected ZIP and attempt WinGet reinstall where package IDs are known?"
            : "Restore from the selected ZIP?";

        var confirm = MessageBox.Show(this, restoreMessage, "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusyState(true);
            Log($"Starting restore from {dialog.FileName}");

            var restoreService = new RestoreService(_registryService, _winGetService);
            var progress = new Progress<string>(Log);
            await restoreService.RestoreAsync(dialog.FileName, UseWingetCheckBox.IsChecked == true, progress);

            Log("Restore completed.");
            MessageBox.Show(this, "Restore completed. Review the log for warnings.", "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Restore failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool isBusy)
    {
        ScanButton.IsEnabled = !isBusy;
        SelectSupportedButton.IsEnabled = !isBusy;
        ClearSelectionButton.IsEnabled = !isBusy;
        BackupButton.IsEnabled = !isBusy;
        RestoreButton.IsEnabled = !isBusy;
        AppsDataGrid.IsEnabled = !isBusy;
        Mouse.OverrideCursor = isBusy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        });
    }

    private void UpdateSummary()
    {
        var selectedCount = _apps.Count(app => app.IsSelected);
        var supportedCount = _apps.Count(app => app.Supported);
        SummaryTextBlock.Text = $"Found: {_apps.Count}   Supported: {supportedCount}   Selected: {selectedCount}";
    }
}
