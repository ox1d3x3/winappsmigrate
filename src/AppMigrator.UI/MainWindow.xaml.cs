using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using AppMigrator.UI.Models;
using AppMigrator.UI.Services;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace AppMigrator.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DiscoveredApp> _apps = new();
    private readonly KnownRuleRepository _ruleRepository = new();
    private readonly RegistryService _registryService = new();
    private readonly WinGetService _winGetService = new();
    private readonly ProcessMonitorService _processMonitorService;
    private readonly UpdateService _updateService = new();
    private readonly UserSettingsService _userSettingsService = new();
    private DateTime _lastProgressStamp = DateTime.UtcNow;
    private long _lastProgressBytes;
    private bool _themeLoaded;
    private string _currentTheme = "Light";

    public MainWindow()
    {
        _processMonitorService = new ProcessMonitorService(_ruleRepository);
        InitializeComponent();

        Title = AppMetadata.DisplayTitle;
        VersionTextBlock.Text = $"Version {AppMetadata.Version}";
        AuthorTextBlock.Text = $"Author: {AppMetadata.Author}";
        ProjectButton.IsEnabled = !string.IsNullOrWhiteSpace(AppMetadata.ProjectUrl);
        AppsDataGrid.ItemsSource = _apps;
        CollectionViewSource.GetDefaultView(AppsDataGrid.ItemsSource).Filter = AppFilter;
        Loaded += MainWindow_Loaded;

        ApplyTheme("Light");
        UpdateSummary();
        Log($"{AppMetadata.DisplayTitle} ready.");
        Log("Use Scan Installed Apps to classify what can be migrated cleanly.");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await _userSettingsService.LoadAsync();
        var desiredTheme = string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        SetThemeSelection(desiredTheme);
        ApplyTheme(desiredTheme);
        _themeLoaded = true;
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
               || app.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
               || app.RuleId.Contains(search, StringComparison.OrdinalIgnoreCase);
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
            ? "Restore from the selected ZIP and install missing apps with WinGet when possible?"
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
                prompt.ApplyTheme(GetSelectedTheme());
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

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshFilter();

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
            MessageBox.Show(this, result.Message, "Latest build", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void AppCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
                if (current is Button || current is TextBox || current is ScrollBar || current is ComboBox || current is CheckBox || current is RadioButton)
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

    private void ThemeLightRadio_Checked(object sender, RoutedEventArgs e) => ChangeTheme("Light");

    private void ThemeDarkRadio_Checked(object sender, RoutedEventArgs e) => ChangeTheme("Dark");

    private void ChangeTheme(string themeName)
    {
        ApplyTheme(themeName);
        if (_themeLoaded)
        {
            _ = _userSettingsService.SaveAsync(new UserSettings { Theme = themeName });
        }
    }

    private string GetSelectedTheme() => _currentTheme;

    private void SetThemeSelection(string themeName)
    {
        if (string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            DarkThemeRadio.IsChecked = true;
        }
        else
        {
            LightThemeRadio.IsChecked = true;
        }
    }

    private void ApplyTheme(string themeName)
    {
        var dark = string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase);
        _currentTheme = dark ? "Dark" : "Light";

        try
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
        }
        catch
        {
        }

        SetBrush("PageBackgroundBrush", dark ? "#10131A" : "#F4F6FB");
        SetBrush("ShellBrush", dark ? "#151A22" : "#EDF2FA");
        SetBrush("SurfaceBrush", dark ? "#1A202B" : "#FFFFFF");
        SetBrush("SurfaceAltBrush", dark ? "#202A37" : "#F7F9FE");
        SetBrush("SurfacePopBrush", dark ? "#18202B" : "#FFFFFF");
        SetBrush("TonalBrush", dark ? "#22324A" : "#E8EEFF");
        SetBrush("TonalStrongBrush", dark ? "#2A3C58" : "#D9E6FF");
        SetBrush("StrokeBrush", dark ? "#2F3C4D" : "#D8DFEC");
        SetBrush("StrokeStrongBrush", dark ? "#475569" : "#C5D1E2");
        SetBrush("PrimaryBrush", dark ? "#9AB1FF" : "#3F66F1");
        SetBrush("PrimaryContainerBrush", dark ? "#243554" : "#E4EBFF");
        SetBrush("SecondaryContainerBrush", dark ? "#213241" : "#E7F3FF");
        SetBrush("SuccessBrush", dark ? "#7CE0AE" : "#147D52");
        SetBrush("SuccessContainerBrush", dark ? "#1D3B31" : "#DCF7EA");
        SetBrush("WarningBrush", dark ? "#FFC06A" : "#B96B00");
        SetBrush("WarningContainerBrush", dark ? "#3A2B15" : "#FFF1DA");
        SetBrush("TextStrongBrush", dark ? "#F5F7FB" : "#162033");
        SetBrush("TextMutedBrush", dark ? "#B3C0D4" : "#64748B");
        SetBrush("TextSoftBrush", dark ? "#8F9DB4" : "#91A0B5");

        Background = (System.Windows.Media.Brush)Resources["PageBackgroundBrush"];
        LogTextBox.CaretBrush = (System.Windows.Media.Brush)Resources["TextStrongBrush"];
    }

    private void SetBrush(string resourceKey, string color)
        => Resources[resourceKey] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

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
        LightThemeRadio.IsEnabled = !isBusy;
        DarkThemeRadio.IsEnabled = !isBusy;
        AppsDataGrid.IsEnabled = !isBusy;
        Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
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
