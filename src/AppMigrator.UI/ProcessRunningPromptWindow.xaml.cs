using System;
using System.Windows;
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
