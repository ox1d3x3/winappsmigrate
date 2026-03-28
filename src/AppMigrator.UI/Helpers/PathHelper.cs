using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Helpers;

public static class PathHelper
{
    public static string ExpandVariables(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        return Environment.ExpandEnvironmentVariables(input);
    }

    public static string MakeSafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "item" : cleaned;
    }

    public static bool ShouldExclude(string fullPath, IEnumerable<string> globPatterns)
    {
        foreach (var pattern in globPatterns)
        {
            if (GlobMatch(fullPath, pattern))
            {
                return true;
            }
        }

        return false;
    }

    public static string RemapToCurrentMachine(string originalPath, MachineProfile sourceMachine)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return originalPath;
        }

        var currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var currentLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var currentRoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var result = originalPath;

        if (!string.IsNullOrWhiteSpace(sourceMachine.UserProfilePath)
            && result.StartsWith(sourceMachine.UserProfilePath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(sourceMachine.UserProfilePath, result);
            return Path.Combine(currentUserProfile, relative);
        }

        if (!string.IsNullOrWhiteSpace(sourceMachine.LocalAppDataPath)
            && result.StartsWith(sourceMachine.LocalAppDataPath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(sourceMachine.LocalAppDataPath, result);
            return Path.Combine(currentLocalAppData, relative);
        }

        if (!string.IsNullOrWhiteSpace(sourceMachine.RoamingAppDataPath)
            && result.StartsWith(sourceMachine.RoamingAppDataPath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(sourceMachine.RoamingAppDataPath, result);
            return Path.Combine(currentRoamingAppData, relative);
        }

        var sourceUserPrefix = $@"C:\Users\{sourceMachine.UserName}\";
        if (!string.IsNullOrWhiteSpace(sourceMachine.UserName)
            && result.StartsWith(sourceUserPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var relative = result[sourceUserPrefix.Length..];
            return Path.Combine(currentUserProfile, relative);
        }

        return result;
    }

    private static bool GlobMatch(string input, string pattern)
    {
        var normalizedInput = input.Replace('/', '\\');
        var normalizedPattern = pattern.Replace('/', '\\');
        var regexPattern = "^" + Regex.Escape(normalizedPattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^\\\\]*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(normalizedInput, regexPattern, RegexOptions.IgnoreCase);
    }
}
