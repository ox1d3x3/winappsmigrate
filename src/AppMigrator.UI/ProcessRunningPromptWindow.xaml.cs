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
        Background = BrushFrom(dark ? "#0B1220" : "#F6F8FE");
        ShellBorder.Background = BrushFrom(dark ? "#101828" : "#FFFFFF");
        ShellBorder.BorderBrush = BrushFrom(dark ? "#26344A" : "#D9E0EE");
        AlertBadge.Background = BrushFrom(dark ? "#382510" : "#FFF4E6");
        ProcessesBorder.Background = BrushFrom(dark ? "#131D31" : "#FFFFFF");
        ProcessesBorder.BorderBrush = BrushFrom(dark ? "#26344A" : "#D9E0EE");
        TimerBorder.Background = BrushFrom(dark ? "#17243B" : "#EEF2FF");
        TimerBorder.BorderBrush = BrushFrom(dark ? "#26344A" : "#D9E0EE");

        var strong = BrushFrom(dark ? "#F4F7FF" : "#172033");
        var muted = BrushFrom(dark ? "#AFBBD0" : "#5C6A82");
        var primary = BrushFrom(dark ? "#9AA8FF" : "#4F46E5");

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

    private static SolidColorBrush BrushFrom(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

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
