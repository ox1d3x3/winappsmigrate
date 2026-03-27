using AppMigrator.UI.Helpers;

namespace AppMigrator.UI.Helpers;

public static class FileCopyHelper
{
    public static void CopyDirectory(string sourceDir, string targetDir, IEnumerable<string> excludeGlobs, IProgress<string>? progress = null)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists)
        {
            return;
        }

        Directory.CreateDirectory(targetDir);

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
        }
    }

    public static void CopyFile(string sourceFile, string targetFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.Copy(sourceFile, targetFile, true);
    }
}
