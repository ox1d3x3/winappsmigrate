using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AppMigrator.UI.Services;

public sealed class ChocolateyService
{
    public bool IsAvailable()
        => IsAvailableAsync().GetAwaiter().GetResult();

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var result = await RunAsync("--version", timeout: TimeSpan.FromSeconds(6)).ConfigureAwait(false);
            return result.Succeeded;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool Succeeded, string Message)> InstallByIdAsync(string packageId, IProgress<string>? liveOutput = null)
    {
        var safeId = EscapeArg(packageId);
        return await RunAsync($"install {safeId} -y --no-progress", liveOutput, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
    }

    public async Task<(bool Found, string? PackageId, string Message)> FindPackageIdByNameAsync(string appName, IProgress<string>? liveOutput = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return (false, null, "App name is empty.");
        }

        var searchName = SimplifySearchName(appName);
        var exact = await RunAsync($"search "{EscapeArg(searchName)}" --exact --limit-output", liveOutput, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        var exactId = TryParsePackageId(exact.Message, searchName);
        if (exactId is not null)
        {
            return (true, exactId, $"Matched Chocolatey package: {exactId}");
        }

        var broad = await RunAsync($"search "{EscapeArg(searchName)}" --limit-output", liveOutput, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var broadId = TryParsePackageId(broad.Message, searchName);
        return broadId is not null
            ? (true, broadId, $"Matched Chocolatey package: {broadId}")
            : (false, null, string.IsNullOrWhiteSpace(broad.Message) ? exact.Message : broad.Message);
    }

    private static string EscapeArg(string value) => value.Replace(""", string.Empty);

    private static string SimplifySearchName(string value)
    {
        var simplified = value;
        simplified = Regex.Replace(simplified, @"\([^\)]*\)", " ");
        simplified = Regex.Replace(simplified, @"\d+(?:[\._@-]\d+)*", " ");
        simplified = Regex.Replace(simplified, @"(x64|x86|arm64|en-us|en us|edition|portable|setup|installer|pro)", " ", RegexOptions.IgnoreCase);
        simplified = Regex.Replace(simplified, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(simplified) ? value.Trim() : simplified;
    }

    private static string? TryParsePackageId(string rawOutput, string appName)
    {
        var normalizedApp = Normalize(appName);
        var bestPackageId = default(string);
        var bestScore = 0;

        foreach (var line in SanitizeMessage(rawOutput).Split(new[] { "
", "
" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var packageId = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                continue;
            }

            var normalizedPackage = Normalize(packageId);
            var score = Score(normalizedApp, normalizedPackage);
            if (score > bestScore)
            {
                bestScore = score;
                bestPackageId = packageId;
            }
        }

        return bestScore >= 40 ? bestPackageId : null;
    }

    private static int Score(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (actual.Contains(expected, StringComparison.OrdinalIgnoreCase) || expected.Contains(actual, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        var expectedTokens = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actualTokens = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var overlap = expectedTokens.Intersect(actualTokens, StringComparer.OrdinalIgnoreCase).Count();
        return overlap * 20;
    }

    private static string Normalize(string value)
        => Regex.Replace(value, @"[^a-zA-Z0-9]+", " ").Trim().ToLowerInvariant();

    private static string SanitizeMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var cleaned = raw.Replace("", string.Empty);
        cleaned = Regex.Replace(cleaned, "\x1B\[[0-9;?]*[ -/]*[@-~]", string.Empty);

        var builder = new StringBuilder();
        foreach (var line in cleaned.Split('
'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("Chocolatey v", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("packages found", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("No packages found", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("See the log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.AppendLine(trimmed);
        }

        return builder.ToString().Trim();
    }

    private static async Task<(bool Succeeded, string Message)> RunAsync(string arguments, IProgress<string>? liveOutput = null, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "choco",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var outputLines = new List<string>();

        process.OutputDataReceived += (_, e) => CaptureOutput(e.Data, outputLines, liveOutput);
        process.ErrorDataReceived += (_, e) => CaptureOutput(e.Data, outputLines, liveOutput);

        if (!process.Start())
        {
            return (false, "Failed to start Chocolatey.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var waitForExitTask = process.WaitForExitAsync();
        if (timeout.HasValue)
        {
            var completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(timeout.Value)).ConfigureAwait(false);
            if (completedTask != waitForExitTask)
            {
                TryKill(process);
                return (false, $"Chocolatey command timed out after {timeout.Value.TotalSeconds:0} seconds.");
            }
        }

        await waitForExitTask.ConfigureAwait(false);

        string message;
        lock (outputLines)
        {
            message = string.Join(Environment.NewLine, outputLines);
        }

        message = SanitizeMessage(message);

        return process.ExitCode == 0
            ? (true, string.IsNullOrWhiteSpace(message) ? "Command completed successfully." : message)
            : (false, string.IsNullOrWhiteSpace(message) ? "Chocolatey command failed." : message);
    }

    private static void CaptureOutput(string? rawLine, List<string> outputLines, IProgress<string>? liveOutput)
    {
        var cleaned = CleanLiveOutputLine(rawLine);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return;
        }

        lock (outputLines)
        {
            outputLines.Add(cleaned);
        }

        liveOutput?.Report(cleaned);
    }

    private static string CleanLiveOutputLine(string? rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(rawLine.Trim(), "\x1B\[[0-9;?]*[ -/]*[@-~]", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        if (cleaned.StartsWith("Chocolatey v", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return cleaned;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }
    }
}
