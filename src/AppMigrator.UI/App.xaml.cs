using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AppMigrator.UI;

public partial class App : System.Windows.Application
{
    private static string CrashLogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinAppsMigrator", "startup-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();

        try
        {
            base.OnStartup(e);
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            WriteCrashLog("Startup failure", ex);
            MessageBox.Show($"Win Apps Migrator could not start.\n\nA crash log was written to:\n{CrashLogPath}\n\n{ex.Message}", "Startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"An unexpected error occurred.\n\nA crash log was written to:\n{CrashLogPath}\n\n{args.Exception.Message}", "Unhandled error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception.");
            WriteCrashLog("AppDomain.CurrentDomain.UnhandledException", ex);
        };
    }

    private static void WriteCrashLog(string heading, Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {heading}");
            sb.AppendLine(ex.ToString());
            sb.AppendLine();
            File.AppendAllText(CrashLogPath, sb.ToString());
        }
        catch
        {
        }
    }
}
