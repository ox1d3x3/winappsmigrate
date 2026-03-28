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

    public async Task<(bool Succeeded, string Message)> InstallWithRecoveryAsync(string packageId)
    {
        var install = await InstallByIdAsync(packageId);
        if (install.Succeeded)
        {
            return install;
        }

        if (NeedsSourceRecovery(install.Message))
        {
            await ResetSourcesAsync();
            install = await InstallByIdAsync(packageId);
        }

        return install;
    }

    public async Task<(bool Succeeded, string Message)> InstallByIdAsync(string packageId)
    {
        var escapedPackageId = packageId.Replace("\"", string.Empty);
        return await RunAsync($"install --id \"{escapedPackageId}\" -e --source winget --silent --disable-interactivity --accept-source-agreements --accept-package-agreements");
    }

    public async Task<(bool Found, string? PackageId, string Message)> FindPackageIdByNameAsync(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return (false, null, "App name is empty.");
        }

        var exact = await RunAsync($"search --name \"{EscapeArg(appName)}\" --exact --source winget --accept-source-agreements");
        var exactId = TryParsePackageId(exact.Message, appName);
        if (exactId is not null)
        {
            return (true, exactId, $"Matched WinGet package: {exactId}");
        }

        var broad = await RunAsync($"search \"{EscapeArg(appName)}\" --source winget --accept-source-agreements");
        var broadId = TryParsePackageId(broad.Message, appName);
        return broadId is not null
            ? (true, broadId, $"Matched WinGet package: {broadId}")
            : (false, null, SanitizeMessage(broad.Message));
    }

    public async Task ResetSourcesAsync()
    {
        await RunAsync("source reset --force");
        await RunAsync("source update");
    }

    private static bool NeedsSourceRecovery(string message)
        => message.Contains("Failed when opening source(s)", StringComparison.OrdinalIgnoreCase)
           || message.Contains("Failed when searching source", StringComparison.OrdinalIgnoreCase)
           || message.Contains("0x8a15005e", StringComparison.OrdinalIgnoreCase);

    private static string EscapeArg(string value) => value.Replace("\"", string.Empty);

    private static string? TryParsePackageId(string rawOutput, string appName)
    {
        var lines = SanitizeMessage(rawOutput)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
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

            var parts = Regex.Split(line, "\\s{2,}").Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (parts.Length >= 2)
            {
                if (parts[0].Contains(appName, StringComparison.OrdinalIgnoreCase) || appName.Contains(parts[0], StringComparison.OrdinalIgnoreCase))
                {
                    return parts[1].Trim();
                }
            }
        }

        return null;
    }

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

            if (Regex.IsMatch(trimmed, @"^[\\|/\\-]+$"))
            {
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^[\u2580-\u259F\s\.:%/\\-]+$"))
            {
                continue;
            }

            builder.AppendLine(trimmed);
        }

        return builder.ToString().Trim();
    }

    private static async Task<(bool Succeeded, string Message)> RunAsync(string arguments)
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

        using var process = Process.Start(psi);
        if (process is null)
        {
            return (false, "Failed to start winget.");
        }

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var message = string.IsNullOrWhiteSpace(stdErr) ? stdOut : $"{stdOut}\n{stdErr}";
        message = SanitizeMessage(message);

        return process.ExitCode == 0
            ? (true, string.IsNullOrWhiteSpace(message) ? "Command completed successfully." : message)
            : (false, string.IsNullOrWhiteSpace(message) ? "WinGet command failed." : message);
    }
}
