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
    private static readonly Regex AnsiRegex = new("\\x1B\\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex SpinnerRegex = new(@"^[\|/\\-]+$", RegexOptions.Compiled);
    private static readonly Regex ProgressBarRegex = new(@"^[\u2580-\u259F\s\.:%/\\-]+$", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"\b\d+(?:[\._@-]\d+)*\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DescriptorRegex = new(@"\b(x64|x86|arm64|en-us|en us|edition|portable|setup|installer|pro)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ParenthesesRegex = new(@"\([^\)]*\)", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumericRegex = new(@"[^a-zA-Z0-9]+", RegexOptions.Compiled);

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
        var escapedPackageId = EscapeArg(packageId);
        var args = $"install --id \"{escapedPackageId}\" -e --source winget --silent --disable-interactivity --accept-source-agreements --accept-package-agreements";
        return await RunAsync(args, liveOutput, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
    }

    public async Task<(bool Found, string? PackageId, string Message)> FindPackageIdByNameAsync(string appName, IProgress<string>? liveOutput = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return (false, null, "App name is empty.");
        }

        var searchName = SimplifySearchName(appName);

        var exactArgs = $"search --name \"{EscapeArg(searchName)}\" --exact --source winget --accept-source-agreements";
        var exact = await RunAsync(exactArgs, liveOutput, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        var exactId = TryParsePackageId(exact.Message, searchName);
        if (exactId is not null)
        {
            return (true, exactId, $"Matched WinGet package: {exactId}");
        }

        var broadArgs = $"search \"{EscapeArg(searchName)}\" --source winget --accept-source-agreements";
        var broad = await RunAsync(broadArgs, liveOutput, TimeSpan.FromSeconds(12)).ConfigureAwait(false);
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

    private static string EscapeArg(string value) => value.Replace("\"", string.Empty);

    private static string SimplifySearchName(string value)
    {
        var simplified = value;
        simplified = ParenthesesRegex.Replace(simplified, " ");
        simplified = VersionRegex.Replace(simplified, " ");
        simplified = DescriptorRegex.Replace(simplified, " ");
        simplified = Regex.Replace(simplified, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(simplified) ? value.Trim() : simplified;
    }

    private static string? TryParsePackageId(string rawOutput, string appName)
    {
        var normalizedApp = Normalize(appName);
        var bestPackageId = default(string);
        var bestScore = 0;

        var lines = SanitizeMessage(rawOutput)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        foreach (var line in lines)
        {
            if (line.StartsWith("Name", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("-", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Found ", StringComparison.OrdinalIgnoreCase)
                || (line.Contains("Source", StringComparison.OrdinalIgnoreCase) && line.Contains("Id", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var parts = MultiSpaceRegex.Split(line).Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
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
        => NonAlphaNumericRegex.Replace(value, " ").Trim().ToLowerInvariant();

    private static string SanitizeMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var cleaned = raw.Replace("\r", string.Empty);
        cleaned = AnsiRegex.Replace(cleaned, string.Empty);

        var builder = new StringBuilder();
        foreach (var line in cleaned.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (SpinnerRegex.IsMatch(trimmed) || ProgressBarRegex.IsMatch(trimmed))
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

        var cleaned = AnsiRegex.Replace(rawLine.Trim(), string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        if (ProgressBarRegex.IsMatch(cleaned) || SpinnerRegex.IsMatch(cleaned))
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
