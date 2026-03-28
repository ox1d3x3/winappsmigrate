using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AppMigrator.UI.Services;

public sealed class RegistryService
{
    public async Task<(bool Succeeded, string? Error)> ExportKeyAsync(string registryKeyPath, string outputFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        return await RunAsync($"export \"{registryKeyPath}\" \"{outputFile}\" /y");
    }

    public async Task<(bool Succeeded, string? Error)> ImportKeyAsync(string regFile)
        => await RunAsync($"import \"{regFile}\"");

    private static async Task<(bool Succeeded, string? Error)> RunAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return (false, "Failed to start reg.exe.");
        }

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var message = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;

        return process.ExitCode == 0
            ? (true, null)
            : (false, message);
    }
}
