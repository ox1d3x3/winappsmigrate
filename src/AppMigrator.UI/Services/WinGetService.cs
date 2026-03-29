using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AppMigrator.UI.Services;

public sealed class WinGetService
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

    public async Task<(bool Succeeded, string Message)> InstallWithRecoveryAsync(string packageId, IProgress<string>? liveOutput = null)
    {
        var install = await InstallByIdAsync(packageId, liveOutput).ConfigureAwait(false);
        if (install.Succeeded)
        {
            return install;
        }

        if (NeedsSourceRecovery(install.Message))
        {
            liveOutput?.Report("WinGet source issue detected. Resetting sources and retrying.");
            await ResetSourcesAsync(liveOutput).ConfigureAwait(false);
            install = await InstallByIdAsync(packageId, liveOutput).ConfigureAwait(false);
        }

        return install;
    }

    public async Task<(bool Succeeded, string Message)> InstallByIdAsync(string packageId, IProgress<string>? liveOutput = null)
    {
        var escapedPackageId = packageId.Replace(""", string.Empty);
        return await RunAsync($"install --id "{escapedPackageId}" -e --source winget --silent --disable-interactivity --accept-source-agreements --accept-package-agreements", liveOutput, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
    }

    public async Task<(bool Found, string? PackageId, string Message)> FindPackageIdByNameAsync(string appName, IProgress<string>? liveOutput = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return (false, null, "App name is empty.");
        }

        var searchName = SimplifySearchName(appName);
        var exact = await RunAsync($"search --name "{EscapeArg(searchName)}" --exact --source winget --accept-source-agreements", liveOutput, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        var exactId = TryParsePackageId(exact.Message, searchName);
        if (exactId is not null)
        {
            return (true, exactId, $"Matched WinGet package: {exactId}");
        }

        var broad = await RunAsync($"search "{EscapeArg(searchName)}" --source winget --accept-source-agreements", liveOutput, TimeSpan.FromSeconds(12)).ConfigureAwait(false);
        var broadId = TryParsePackageId(broad.Message, searchName);
        return broadId is not null
            ? (true, broadId, $"Matched WinGet package: {broadId}")
            : (false, null, string.IsNullOrWhiteSpace(broad.Message) ? SanitizeMessage(exact.Message) : SanitizeMessage(broad.Message));
    }

    public async Task ResetSourcesAsync(IProgress<string>? liveOutput = null)
    {
        await RunAsync("source reset --force", liveOutput, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        await RunAsync("source update", liveOutput, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    }

    private static bool NeedsSourceRecovery(string message)
        => message.Contains("Failed when opening source(s)", StringComparison.OrdinalIgnoreCase)
           || message.Contains("Failed when searching source", StringComparison.OrdinalIgnoreCase)
           || message.Contains("0x8a15005e", StringComparison.OrdinalIgnoreCase);

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

        var lines = SanitizeMessage(rawOutput)
            .Split(new[] { "
", "
" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        foreach (var line in lines)
        {
            if (line.StartsWith("Name", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("-", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Found ", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Source", StringComparison.OrdinalIgnoreCase) && line.Contains("Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = Regex.Split(line, "\s{2,}").Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (parts.Length < 2)
            {
                continue;
            }

            var candidateName = parts[0].Trim();
            var candidateId = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(candidateId))
            {
                continue;
            }

            var score = Score(normalizedApp, Normalize(candidateName), Normalize(candidateId));
            if (score > bestScore)
            {
                bestScore = score;
                bestPackageId = candidateId;
            }
        }

        return bestScore >= 45 ? bestPackageId : null;
    }

    private static int Score(string expected, string actualName, string actualId)
    {
        var score = 0;
        if (string.Equals(expected, actualName, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (actualName.Contains(expected, StringComparison.OrdinalIgnoreCase) || expected.Contains(actualName, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (actualId.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        var expectedTokens = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actualTokens = (actualName + " " + actualId).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var overlap = expectedTokens.Intersect(actualTokens, StringComparer.OrdinalIgnoreCase).Count();
        score += overlap * 12;
        return score;
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

            if (Regex.IsMatch(trimmed, @"^[\|/\-]+$") || Regex.IsMatch(trimmed, @"^[▀-▟\s\.:%/\-]+$"))
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
            FileName = "winget",
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
            return (false, "Failed to start winget.");
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
                return (false, $"WinGet command timed out after {timeout.Value.TotalSeconds:0} seconds.");
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
            : (false, string.IsNullOrWhiteSpace(message) ? "WinGet command failed." : message);
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

        if (Regex.IsMatch(cleaned, @"^[▀-▟\s\.:%/\-]+$"))
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
