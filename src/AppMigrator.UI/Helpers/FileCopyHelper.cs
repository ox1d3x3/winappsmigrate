using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
}
