using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Helpers;

public static class FileCopyHelper
{
    public static long GetPathSize(string path, IEnumerable<string> excludeGlobs)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }

        if (!Directory.Exists(path))
        {
            return 0;
        }

        return new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => !PathHelper.ShouldExclude(file.FullName, excludeGlobs))
            .Sum(file => file.Length);
    }

    public static long CopyDirectory(string sourceDir, string targetDir, IEnumerable<string> excludeGlobs, IProgress<string>? progress = null, Action<long>? bytesCopiedCallback = null)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists)
        {
            return 0;
        }

        Directory.CreateDirectory(targetDir);
        var copied = 0L;

        foreach (var directory in source.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if (PathHelper.ShouldExclude(directory.FullName, excludeGlobs))
            {
                progress?.Report($"Skipping directory by rule: {directory.FullName}");
                continue;
            }

            var relative = Path.GetRelativePath(source.FullName, directory.FullName);
            var destination = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(destination);
        }

        foreach (var file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (PathHelper.ShouldExclude(file.FullName, excludeGlobs))
            {
                progress?.Report($"Skipping file by rule: {file.FullName}");
                continue;
            }

            var relative = Path.GetRelativePath(source.FullName, file.FullName);
            var destination = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            file.CopyTo(destination, true);
            copied += file.Length;
            bytesCopiedCallback?.Invoke(file.Length);
        }

        return copied;
    }

    public static long CopyFile(string sourceFile, string targetFile, Action<long>? bytesCopiedCallback = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.Copy(sourceFile, targetFile, true);
        var length = new FileInfo(sourceFile).Length;
        bytesCopiedCallback?.Invoke(length);
        return length;
    }

    public static List<BackupFileEntry> BuildVerificationEntries(string sourcePath, IEnumerable<string> excludeGlobs)
    {
        var entries = new List<BackupFileEntry>();

        if (File.Exists(sourcePath))
        {
            var fileInfo = new FileInfo(sourcePath);
            entries.Add(new BackupFileEntry
            {
                RelativePath = string.Empty,
                SizeBytes = fileInfo.Length,
                Sha256 = ComputeSha256(sourcePath)
            });
            return entries;
        }

        if (!Directory.Exists(sourcePath))
        {
            return entries;
        }

        var root = new DirectoryInfo(sourcePath);
        foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories)
                     .Where(file => !PathHelper.ShouldExclude(file.FullName, excludeGlobs))
                     .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(new BackupFileEntry
            {
                RelativePath = Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/'),
                SizeBytes = file.Length,
                Sha256 = ComputeSha256(file.FullName)
            });
        }

        return entries;
    }

    public static (bool Succeeded, List<string> Warnings) VerifyPath(string targetPath, string pathType, IReadOnlyList<BackupFileEntry>? expectedFiles)
    {
        var warnings = new List<string>();
        if (expectedFiles is null || expectedFiles.Count == 0)
        {
            return (true, warnings);
        }

        if (string.Equals(pathType, "file", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(targetPath))
            {
                warnings.Add($"Verification failed: missing file {targetPath}");
                return (false, warnings);
            }

            var fileInfo = new FileInfo(targetPath);
            var expected = expectedFiles[0];
            if (fileInfo.Length != expected.SizeBytes)
            {
                warnings.Add($"Verification failed: size mismatch for {targetPath}");
                return (false, warnings);
            }

            var actualHash = ComputeSha256(targetPath);
            if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Verification failed: hash mismatch for {targetPath}");
                return (false, warnings);
            }

            return (true, warnings);
        }

        if (!Directory.Exists(targetPath))
        {
            warnings.Add($"Verification failed: missing directory {targetPath}");
            return (false, warnings);
        }

        foreach (var expected in expectedFiles)
        {
            var actualPath = string.IsNullOrWhiteSpace(expected.RelativePath)
                ? targetPath
                : Path.Combine(targetPath, expected.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(actualPath))
            {
                warnings.Add($"Verification failed: missing file {actualPath}");
                continue;
            }

            var fileInfo = new FileInfo(actualPath);
            if (fileInfo.Length != expected.SizeBytes)
            {
                warnings.Add($"Verification failed: size mismatch for {actualPath}");
                continue;
            }

            var actualHash = ComputeSha256(actualPath);
            if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Verification failed: hash mismatch for {actualPath}");
            }
        }

        return (warnings.Count == 0, warnings);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
