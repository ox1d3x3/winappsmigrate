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
    {
        try
        {
            var result = RunAsync("--version").GetAwaiter().GetResult();
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
        return await RunAsync($"install {safeId} -y --no-progress", liveOutput);
    }

    public async Task<(bool Found, string? PackageId, string Message)> FindPackageIdByNameAsync(string appName, IProgress<string>? liveOutput = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return (false, null, "App name is empty.");
        }

        var result = await RunAsync($"search \"{EscapeArg(appName)}\" --limit-output", liveOutput);
        if (!result.Succeeded)
        {
            return (false, null, result.Message);
        }

        var packageId = TryParsePackageId(result.Message, appName);
        return packageId is null
            ? (false, null, result.Message)
            : (true, packageId, $"Matched Chocolatey package: {packageId}");
    }

    private static string EscapeArg(string value) => value.Replace("\"", string.Empty);

    private static string? TryParsePackageId(string rawOutput, string appName)
    {
        var normalizedApp = Normalize(appName);
        var bestPackageId = default(string);
        var bestScore = 0;

        foreach (var line in SanitizeMessage(rawOutput).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
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

        var cleaned = raw.Replace("\r", string.Empty);
        cleaned = Regex.Replace(cleaned, "\\x1B\\[[0-9;?]*[ -/]*[@-~]", string.Empty);

        var builder = new StringBuilder();
        foreach (var line in cleaned.Split('\n'))
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

    private static async Task<(bool Succeeded, string Message)> RunAsync(string arguments, IProgress<string>? liveOutput = null)
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

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lock (outputLines)
            {
                outputLines.Add(e.Data);
            }

            liveOutput?.Report(e.Data.Trim());
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lock (outputLines)
            {
                outputLines.Add(e.Data);
            }

            liveOutput?.Report(e.Data.Trim());
        };

        if (!process.Start())
        {
            return (false, "Failed to start Chocolatey.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

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
}
