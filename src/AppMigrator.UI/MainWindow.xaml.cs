using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    private readonly ProcessMonitorService _processMonitorService;
    private readonly UpdateService _updateService = new();
    private DateTime _lastProgressStamp = DateTime.UtcNow;
    private long _lastProgressBytes;

    public MainWindow()
    {
        _processMonitorService = new ProcessMonitorService(_ruleRepository);
        InitializeComponent();
        Title = AppMetadata.DisplayTitle;
        VersionTextBlock.Text = $"Version V{AppMetadata.Version}";
        AuthorTextBlock.Text = $"Author: {AppMetadata.Author}";
        ProjectButton.IsEnabled = !string.IsNullOrWhiteSpace(AppMetadata.ProjectUrl);
        AppsDataGrid.ItemsSource = _apps;
        CollectionViewSource.GetDefaultView(AppsDataGrid.ItemsSource).Filter = AppFilter;
        UpdateSummary();
        Log($"{AppMetadata.DisplayTitle} ready.");
        Log("Use Scan Installed Apps to classify what can be migrated cleanly.");
    }

    private bool AppFilter(object item)
    {
        if (item is not DiscoveredApp app)
        {
            return false;
        }

        if (SupportedOnlyCheckBox.IsChecked == true && !app.Supported)
        {
            return false;
        }

        var search = SearchTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return app.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || app.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase)
               || app.Category.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusyState(true);
            _apps.Clear();
            Log("Scanning installed applications...");

            var discoveryService = new AppDiscoveryService(_ruleRepository);
            var apps = await discoveryService.DiscoverAsync(new Progress<string>(Log));

            foreach (var app in apps)
            {
                app.PropertyChanged += App_PropertyChanged;
                _apps.Add(app);
            }

            RefreshFilter();
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

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiscoveredApp.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void SelectSupportedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _apps.Where(a => AppFilter(a)))
        {
            app.IsSelected = app.Supported;
        }

        UpdateSummary();
        Log("Selected all supported apps in the current filter.");
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

        var readiness = await PrepareAppsForBackupAsync(selected);
        if (readiness.Cancelled)
        {
            Log("Backup cancelled during running-app check.");
            return;
        }

        if (readiness.ReadyApps.Count == 0)
        {
            MessageBox.Show(this, "No apps remain after the running-app checks.", "Nothing to back up", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "ZIP archives (*.zip)|*.zip",
            Title = "Save backup ZIP",
            FileName = $"WinAppsMigrator_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetBusyState(true);
            ResetProgress();
            Log($"Starting backup for {readiness.ReadyApps.Count} app(s).");

            var backupService = new BackupService(_ruleRepository, _registryService);
            await backupService.CreateBackupAsync(readiness.ReadyApps, dialog.FileName, new Progress<MigrationProgressInfo>(HandleProgress), new Progress<string>(Log));

            Log($"Backup completed successfully: {dialog.FileName}");
            MessageBox.Show(this, $"Backup completed successfully.\n\nZIP: {dialog.FileName}\nReport: {Path.ChangeExtension(dialog.FileName, ".backup_report.txt")}", "Backup complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
            ResetProgress();
            Log($"Starting restore from {dialog.FileName}");

            var restoreService = new RestoreService(_registryService, _winGetService);
            await restoreService.RestoreAsync(dialog.FileName, UseWingetCheckBox.IsChecked == true, new Progress<MigrationProgressInfo>(HandleProgress), new Progress<string>(Log));

            Log("Restore completed.");
            MessageBox.Show(this, $"Restore completed.\n\nReport: {Path.Combine(Path.GetDirectoryName(dialog.FileName)!, $"{Path.GetFileNameWithoutExtension(dialog.FileName)}.restore_report.txt")}", "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private async Task<(bool Cancelled, List<DiscoveredApp> ReadyApps)> PrepareAppsForBackupAsync(List<DiscoveredApp> apps)
    {
        var ready = new List<DiscoveredApp>();

        foreach (var app in apps)
        {
            var running = _processMonitorService.GetRunningProcesses(app);
            if (running.Count == 0)
            {
                ready.Add(app);
                continue;
            }

            while (running.Count > 0)
            {
                var prompt = new ProcessRunningPromptWindow(app.DisplayName, string.Join(", ", running), 60) { Owner = this };
                prompt.ShowDialog();

                if (prompt.CancelJob)
                {
                    return (true, ready);
                }

                if (prompt.SkipApp)
                {
                    Log($"Skipped {app.DisplayName} because it remained open.");
                    goto SkipCurrentApp;
                }

                var closed = await _processMonitorService.WaitForProcessesToExitAsync(running, TimeSpan.FromSeconds(1));
                if (closed)
                {
                    running = Array.Empty<string>();
                    break;
                }

                running = _processMonitorService.GetRunningProcesses(app);
            }

            ready.Add(app);
            continue;

        SkipCurrentApp:
            continue;
        }

        return (false, ready);
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => RefreshFilter();

    private void FilterChanged(object sender, RoutedEventArgs e)
        => RefreshFilter();

    private void RefreshFilter()
    {
        CollectionViewSource.GetDefaultView(AppsDataGrid.ItemsSource)?.Refresh();
        UpdateSummary();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusyState(true);
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            Log(result.Message);
            MessageBox.Show(this, result.Message, "Update check", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void AppCard_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement card || card.DataContext is not DiscoveredApp app)
        {
            return;
        }

        if (e.OriginalSource is FrameworkElement element)
        {
            var current = element;
            while (current is not null)
            {
                if (current is Button || current is TextBox || current is ScrollBar)
                {
                    return;
                }

                current = current.Parent as FrameworkElement;
            }
        }

        app.IsSelected = !app.IsSelected;
        UpdateSummary();
        e.Handled = true;
    }

    private void ProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AppMetadata.ProjectUrl))
        {
            MessageBox.Show(this, "Project URL has not been configured yet.", "Project link", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = AppMetadata.ProjectUrl,
            UseShellExecute = true
        });
    }

    private void SetBusyState(bool isBusy)
    {
        ScanButton.IsEnabled = !isBusy;
        SelectSupportedButton.IsEnabled = !isBusy;
        ClearSelectionButton.IsEnabled = !isBusy;
        BackupButton.IsEnabled = !isBusy;
        RestoreButton.IsEnabled = !isBusy;
        UpdateButton.IsEnabled = !isBusy;
        UpdateButtonTop.IsEnabled = !isBusy;
        ProjectButton.IsEnabled = !isBusy && !string.IsNullOrWhiteSpace(AppMetadata.ProjectUrl);
        AppsDataGrid.IsEnabled = !isBusy;
        System.Windows.Input.Mouse.OverrideCursor = isBusy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void HandleProgress(MigrationProgressInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            OperationProgressBar.Value = info.Percent;
            ProgressStageTextBlock.Text = string.IsNullOrWhiteSpace(info.Stage) ? "Working" : info.Stage;
            ProgressMessageTextBlock.Text = info.Message;
            CurrentAppTextBlock.Text = string.IsNullOrWhiteSpace(info.CurrentApp) ? "—" : info.CurrentApp;

            var now = DateTime.UtcNow;
            var deltaBytes = Math.Max(0, info.ProcessedBytes - _lastProgressBytes);
            var deltaSeconds = Math.Max((now - _lastProgressStamp).TotalSeconds, 0.25d);
            var bytesPerSecond = deltaBytes / deltaSeconds;
            _lastProgressStamp = now;
            _lastProgressBytes = info.ProcessedBytes;

            TransferStatsTextBlock.Text = $"{FormatBytes(info.ProcessedBytes)} / {FormatBytes(info.TotalBytes)}";
            if (bytesPerSecond > 0 && info.TotalBytes > info.ProcessedBytes)
            {
                var etaSeconds = (info.TotalBytes - info.ProcessedBytes) / bytesPerSecond;
                EtaTextBlock.Text = $"ETA: {TimeSpan.FromSeconds(etaSeconds):mm\\:ss}  •  {FormatBytes((long)bytesPerSecond)}/s";
            }
            else
            {
                EtaTextBlock.Text = "ETA: --";
            }
        });
    }

    private void ResetProgress()
    {
        OperationProgressBar.Value = 0;
        ProgressStageTextBlock.Text = "Ready";
        ProgressMessageTextBlock.Text = "Close selected apps before backup or restore for best results.";
        CurrentAppTextBlock.Text = "—";
        TransferStatsTextBlock.Text = "0 MB / 0 MB";
        EtaTextBlock.Text = "ETA: --";
        _lastProgressStamp = DateTime.UtcNow;
        _lastProgressBytes = 0;
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
        var visibleCount = CollectionViewSource.GetDefaultView(AppsDataGrid.ItemsSource)?.Cast<object>().Count() ?? _apps.Count;
        SummaryTextBlock.Text = $"Visible: {visibleCount}   Found: {_apps.Count}   Supported: {supportedCount}   Selected: {selectedCount}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var order = 0;
        while (value >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            value /= 1024;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, suffixes[order]);
    }
}
