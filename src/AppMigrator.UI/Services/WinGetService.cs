using System;
using System.Diagnostics;
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
        var install = await InstallAsync(packageId);
        if (install.Succeeded)
        {
            return install;
        }

        if (install.Message.Contains("Failed when opening source(s)", StringComparison.OrdinalIgnoreCase))
        {
            await ResetSourcesAsync();
            install = await InstallAsync(packageId);
        }

        return install;
    }

    public async Task<(bool Succeeded, string Message)> InstallAsync(string packageId)
    {
        var escapedPackageId = packageId.Replace("\"", "");
        return await RunAsync($"install --id \"{escapedPackageId}\" -e --silent --disable-interactivity --accept-source-agreements --accept-package-agreements");
    }

    public async Task ResetSourcesAsync()
    {
        await RunAsync("source reset --force");
        await RunAsync("source update");
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
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return (false, "Failed to start winget.");
        }

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var message = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;

        return process.ExitCode == 0
            ? (true, message)
            : (false, message);
    }
}
