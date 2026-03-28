using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AppMigrator.UI.Services;

public sealed class RegistryService
{
    public async Task<(bool Succeeded, string? Error)> ExportKeyAsync(string registryKeyPath, string outputFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = $"export \"{registryKeyPath}\" \"{outputFile}\" /y",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return (false, "Failed to start reg.exe for export.");
        }

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? (true, null)
            : (false, string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr);
    }

    public async Task<(bool Succeeded, string? Error)> ImportKeyAsync(string regFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = $"import \"{regFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return (false, "Failed to start reg.exe for import.");
        }

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? (true, null)
            : (false, string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr);
    }
}
