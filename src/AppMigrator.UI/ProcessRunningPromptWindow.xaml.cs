using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AppMigrator.UI;

public partial class ProcessRunningPromptWindow : Window
{
    private readonly DispatcherTimer _timer;
    private int _remainingSeconds;

    public bool SkipApp { get; private set; }
    public bool CancelJob { get; private set; }

    public ProcessRunningPromptWindow(string appName, string processList, int seconds)
    {
        InitializeComponent();
        _remainingSeconds = seconds;
        TitleTextBlock.Text = $"{appName} is still running";
        MessageTextBlock.Text = processList;
        CountdownTextBlock.Text = $"Skipping automatically in {_remainingSeconds} seconds.";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    public void ApplyTheme(string themeName)
    {
        var dark = string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase);
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#0B1120" : "#F4F6FB"));
        ShellBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#101828" : "#FFFFFF"));
        ShellBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#243247" : "#DFE6F0"));
        AlertBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#2C231A" : "#FFF7ED"));
        ProcessesBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#111B2E" : "#FBFCFE"));
        ProcessesBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#243247" : "#DFE6F0"));
        TimerBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#182742" : "#EEF4FF"));

        var primary = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#7AA2FF" : "#2E6BFF"));
        var strong = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#F8FAFC" : "#101828"));
        var muted = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#B3C1D9" : "#667085"));

        TitleTextBlock.Foreground = strong;
        SubtitleTextBlock.Foreground = muted;
        ProcessesHeaderTextBlock.Foreground = strong;
        MessageTextBlock.Foreground = muted;
        TimerHeaderTextBlock.Foreground = primary;
        CountdownTextBlock.Foreground = primary;
        HelpTextBlock.Foreground = muted;
        PromptNoteTextBlock.Foreground = muted;
        ActionHintTextBlock.Foreground = muted;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        CountdownTextBlock.Text = _remainingSeconds <= 0
            ? "Skipping now..."
            : $"Skipping automatically in {_remainingSeconds} seconds.";

        if (_remainingSeconds <= 0)
        {
            _timer.Stop();
            SkipApp = true;
            DialogResult = true;
            Close();
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = true;
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        SkipApp = true;
        DialogResult = true;
        Close();
    }

    private void CancelButtonEx_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        CancelJob = true;
        DialogResult = false;
        Close();
    }
}
