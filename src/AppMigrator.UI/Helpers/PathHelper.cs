using System.Text.RegularExpressions;

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
