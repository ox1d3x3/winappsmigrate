using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AppMigrator.UI.Helpers;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Services;

public sealed class KnownRuleRepository
{
    private readonly List<AppRule> _rules;

    public KnownRuleRepository()
    {
        _rules = BuildRules();
        _rules.AddRange(LoadExternalRules());
    }

    public IReadOnlyList<AppRule> GetRules() => _rules;

    public AppRule? Match(string displayName, string publisher = "", string installLocation = "")
    {
        if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(publisher) && string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        var normalizedName = displayName.Trim();
        var best = _rules
            .Select(rule => new { Rule = rule, Score = ScoreRule(rule, normalizedName, publisher, installLocation) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Rule.Confidence)
            .FirstOrDefault();

        return best?.Rule;
    }

    public AppRule? GetById(string ruleId)
        => _rules.FirstOrDefault(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));

    private static double ScoreRule(AppRule rule, string displayName, string publisher, string installLocation)
    {
        var score = 0d;

        foreach (var candidate in rule.MatchDisplayNames.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (string.Equals(displayName, candidate, StringComparison.OrdinalIgnoreCase))
            {
                score += 12d;
            }
            else if (displayName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                score += 6d;
            }
        }

        foreach (var token in rule.MatchTokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(displayName) && displayName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 3.5d;
            }
        }

        foreach (var token in rule.MatchPublisherTokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(publisher) && publisher.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 2.5d;
            }
        }

        foreach (var token in rule.MatchInstallPathTokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(installLocation) && installLocation.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 1.5d;
            }
        }

        return score;
    }

    private static IEnumerable<AppRule> LoadExternalRules()
    {
        var rulesDirectory = Path.Combine(AppContext.BaseDirectory, "rules");
        if (!Directory.Exists(rulesDirectory))
        {
            return Array.Empty<AppRule>();
        }

        var loadedRules = new List<AppRule>();

        foreach (var file in Directory.EnumerateFiles(rulesDirectory, "*.rule.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                var rules = JsonSerializer.Deserialize<List<AppRule>>(json, JsonHelper.DefaultOptions);
                if (rules is null)
                {
                    continue;
                }

                loadedRules.AddRange(rules.Where(rule => !string.IsNullOrWhiteSpace(rule.Id)));
            }
            catch
            {
            }
        }

        return loadedRules;
    }

    private static List<AppRule> BuildRules()
    {
        return new()
        {
            ChromiumRule(
                id: "google.chrome",
                friendlyName: "Google Chrome",
                wingetId: "Google.Chrome",
                displayNames: new() { "Google Chrome" },
                publisherTokens: new() { "Google" },
                installTokens: new() { @"Google\Chrome" },
                processNames: new() { "chrome" },
                dataPath: @"%LOCALAPPDATA%\Google\Chrome\User Data",
                registryKey: @"HKCU\Software\Google\Chrome",
                notes: "Profile restore works best after the same browser is installed on the new machine."),

            ChromiumRule(
                id: "microsoft.edge",
                friendlyName: "Microsoft Edge",
                wingetId: "Microsoft.Edge",
                displayNames: new() { "Microsoft Edge" },
                publisherTokens: new() { "Microsoft" },
                installTokens: new() { @"Microsoft\Edge" },
                processNames: new() { "msedge" },
                dataPath: @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
                registryKey: @"HKCU\Software\Microsoft\Edge",
                notes: "Edge is usually already present, but a matching build is safer before restore."),

            ChromiumRule(
                id: "brave.browser",
                friendlyName: "Brave Browser",
                wingetId: "Brave.Brave",
                displayNames: new() { "Brave", "Brave Browser" },
                publisherTokens: new() { "Brave Software", "Brave" },
                installTokens: new() { @"BraveSoftware\Brave-Browser" },
                processNames: new() { "brave" },
                dataPath: @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data",
                registryKey: @"HKCU\Software\BraveSoftware\Brave-Browser",
                notes: "Brave is treated as a Chromium profile migration."),

            ChromiumRule(
                id: "vivaldi.browser",
                friendlyName: "Vivaldi",
                wingetId: "Vivaldi.Vivaldi",
                displayNames: new() { "Vivaldi" },
                publisherTokens: new() { "Vivaldi" },
                installTokens: new() { @"Vivaldi" },
                processNames: new() { "vivaldi" },
                dataPath: @"%LOCALAPPDATA%\Vivaldi\User Data",
                registryKey: @"HKCU\Software\Vivaldi",
                notes: "Vivaldi follows the same pattern as other Chromium browsers."),

            new AppRule
            {
                Id = "opera.browser",
                FriendlyName = "Opera",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.90,
                WingetId = "Opera.Opera",
                MatchDisplayNames = new() { "Opera", "Opera GX", "Opera Stable" },
                MatchPublisherTokens = new() { "Opera" },
                MatchInstallPathTokens = new() { @"Opera" },
                ProcessNames = new() { "opera", "opera_gx" },
                IncludePaths = new() { @"%APPDATA%\Opera Software\Opera Stable", @"%APPDATA%\Opera Software\Opera GX Stable" },
                RegistryKeys = new() { @"HKCU\Software\Opera Software" },
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new() { "Opera family profile paths are backed up, but reinstall first is still the safe path." }
            },
            new AppRule
            {
                Id = "mozilla.firefox",
                FriendlyName = "Mozilla Firefox",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.93,
                WingetId = "Mozilla.Firefox",
                MatchDisplayNames = new() { "Mozilla Firefox", "Firefox" },
                MatchPublisherTokens = new() { "Mozilla" },
                MatchInstallPathTokens = new() { @"Mozilla Firefox" },
                ProcessNames = new() { "firefox" },
                IncludePaths = new() { @"%APPDATA%\Mozilla\Firefox", @"%LOCALAPPDATA%\Mozilla\Firefox" },
                RegistryKeys = new() { @"HKCU\Software\Mozilla" },
                ExcludeGlobs = new() { @"**\cache2\**", @"**\startupCache\**" },
                Notes = new() { "Best restored after Firefox is installed on the new machine." }
            },
            new AppRule
            {
                Id = "vscode",
                FriendlyName = "Visual Studio Code",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.92,
                WingetId = "Microsoft.VisualStudioCode",
                MatchDisplayNames = new() { "Microsoft Visual Studio Code", "Visual Studio Code" },
                MatchPublisherTokens = new() { "Microsoft" },
                MatchInstallPathTokens = new() { @"Microsoft VS Code", @"Visual Studio Code" },
                ProcessNames = new() { "Code", "Code - Insiders" },
                IncludePaths = new() { @"%APPDATA%\Code\User", @"%USERPROFILE%\.vscode\extensions", @"%APPDATA%\Code - Insiders\User", @"%USERPROFILE%\.vscode-insiders\extensions" },
                RegistryKeys = new() { @"HKCU\Software\Microsoft\VSCommon" },
                ExcludeGlobs = new() { @"**\CachedData\**", @"**\Cache\**" },
                Notes = new() { "Extensions and user settings restore best into an already installed VS Code environment." }
            },
            new AppRule
            {
                Id = "obsidian",
                FriendlyName = "Obsidian",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.90,
                WingetId = "Obsidian.Obsidian",
                MatchDisplayNames = new() { "Obsidian" },
                MatchPublisherTokens = new() { "Obsidian" },
                MatchInstallPathTokens = new() { @"Obsidian" },
                ProcessNames = new() { "Obsidian" },
                IncludePaths = new() { @"%APPDATA%\Obsidian" },
                RegistryKeys = new() { @"HKCU\Software\Obsidian" },
                Notes = new() { "Vault content outside the profile folder is still your responsibility to back up separately." }
            },
            new AppRule
            {
                Id = "notepadpp",
                FriendlyName = "Notepad++",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.91,
                WingetId = "Notepad++.Notepad++",
                MatchDisplayNames = new() { "Notepad++" },
                MatchPublisherTokens = new() { "Notepad++" },
                MatchInstallPathTokens = new() { @"Notepad++" },
                ProcessNames = new() { "notepad++" },
                IncludePaths = new() { @"%APPDATA%\Notepad++" },
                RegistryKeys = new() { @"HKCU\Software\Notepad++" },
                Notes = new() { "Typical user settings and session state are captured." }
            },
            new AppRule
            {
                Id = "filezilla",
                FriendlyName = "FileZilla",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.91,
                WingetId = "FileZilla.FileZilla",
                MatchDisplayNames = new() { "FileZilla", "FileZilla Pro" },
                MatchPublisherTokens = new() { "FileZilla" },
                MatchInstallPathTokens = new() { @"FileZilla" },
                ProcessNames = new() { "filezilla" },
                IncludePaths = new() { @"%APPDATA%\FileZilla" },
                RegistryKeys = new() { @"HKCU\Software\FileZilla" },
                Notes = new() { "Site manager data may contain sensitive server details and credentials." }
            },
            new AppRule
            {
                Id = "putty",
                FriendlyName = "PuTTY",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.89,
                WingetId = "PuTTY.PuTTY",
                MatchDisplayNames = new() { "PuTTY", "PuTTY release 0.83 (64-bit)" },
                MatchPublisherTokens = new() { "Simon Tatham" },
                MatchInstallPathTokens = new() { @"PuTTY" },
                ProcessNames = new() { "putty" },
                RegistryKeys = new() { @"HKCU\Software\SimonTatham\PuTTY" },
                Notes = new() { "PuTTY stores most settings in the HKCU registry." }
            },
            new AppRule
            {
                Id = "windows.terminal",
                FriendlyName = "Windows Terminal",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.87,
                WingetId = "Microsoft.WindowsTerminal",
                MatchDisplayNames = new() { "Windows Terminal" },
                MatchPublisherTokens = new() { "Microsoft" },
                MatchInstallPathTokens = new() { @"WindowsTerminal" },
                ProcessNames = new() { "WindowsTerminal" },
                IncludePaths = new() { @"%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState" },
                Notes = new() { "This restores profile data only, not the whole Store app package." }
            },
            new AppRule
            {
                Id = "git",
                FriendlyName = "Git",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.85,
                WingetId = "Git.Git",
                MatchDisplayNames = new() { "Git version" },
                MatchPublisherTokens = new() { "Git for Windows", "The Git Development Community" },
                MatchInstallPathTokens = new() { @"Git" },
                ProcessNames = new() { "git-bash", "git-gui" },
                IncludePaths = new() { @"%USERPROFILE%\.gitconfig", @"%USERPROFILE%\.git-credentials" },
                RegistryKeys = new() { @"HKCU\Software\GitForWindows" },
                Notes = new() { "Credentials may contain sensitive information." }
            },
            new AppRule
            {
                Id = "teracopy",
                FriendlyName = "TeraCopy",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.82,
                WingetId = "CodeSector.TeraCopy",
                MatchDisplayNames = new() { "TeraCopy" },
                MatchPublisherTokens = new() { "Code Sector" },
                MatchInstallPathTokens = new() { @"TeraCopy" },
                ProcessNames = new() { "TeraCopy" },
                IncludePaths = new() { @"%APPDATA%\TeraCopy" },
                RegistryKeys = new() { @"HKCU\Software\CodeSector\TeraCopy" },
                Notes = new() { "Settings migration only. Shell integration still depends on reinstall." }
            },
            new AppRule
            {
                Id = "sevenzip",
                FriendlyName = "7-Zip",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.84,
                WingetId = "7zip.7zip",
                MatchDisplayNames = new() { "7-Zip", "7-Zip 25.01 (x64)" },
                MatchPublisherTokens = new() { "Igor Pavlov" },
                MatchInstallPathTokens = new() { @"7-Zip" },
                ProcessNames = new() { "7zFM" },
                IncludePaths = new() { @"%APPDATA%\7-Zip" },
                RegistryKeys = new() { @"HKCU\Software\7-Zip" },
                Notes = new() { "This focuses on user settings, not shell extension registration." }
            },
            new AppRule
            {
                Id = "greenshot",
                FriendlyName = "Greenshot",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.83,
                WingetId = "Greenshot.Greenshot",
                MatchDisplayNames = new() { "Greenshot" },
                MatchPublisherTokens = new() { "Greenshot" },
                MatchInstallPathTokens = new() { @"Greenshot" },
                ProcessNames = new() { "Greenshot" },
                IncludePaths = new() { @"%APPDATA%\Greenshot" },
                RegistryKeys = new() { @"HKCU\Software\Greenshot" },
                Notes = new() { "Greenshot settings and destinations are backed up." }
            },
            new AppRule
            {
                Id = "everything",
                FriendlyName = "Everything",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.80,
                WingetId = "voidtools.Everything",
                MatchDisplayNames = new() { "Everything", "Everything 1.4.1.1030 (x64)" },
                MatchPublisherTokens = new() { "voidtools" },
                MatchInstallPathTokens = new() { @"Everything" },
                ProcessNames = new() { "Everything" },
                IncludePaths = new() { @"%APPDATA%\Everything" },
                RegistryKeys = new() { @"HKCU\Software\voidtools\Everything" },
                Notes = new() { "This restores user preferences, not the indexed database service state." }
            },
            new AppRule
            {
                Id = "discord",
                FriendlyName = "Discord",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.78,
                WingetId = "Discord.Discord",
                MatchDisplayNames = new() { "Discord" },
                MatchPublisherTokens = new() { "Discord" },
                MatchInstallPathTokens = new() { @"Discord" },
                ProcessNames = new() { "Discord" },
                IncludePaths = new() { @"%APPDATA%\discord" },
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new() { "Discord can be restored as profile data, but sign-in tokens may not survive across machines." }
            },
            new AppRule
            {
                Id = "adobe.acrobat",
                FriendlyName = "Adobe Acrobat",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.76,
                WingetId = "Adobe.Acrobat.Reader.64-bit",
                MatchDisplayNames = new() { "Adobe Acrobat", "Adobe Acrobat (64-bit)", "Adobe Acrobat Reader" },
                MatchPublisherTokens = new() { "Adobe" },
                MatchInstallPathTokens = new() { @"Adobe\Acrobat" },
                ProcessNames = new() { "Acrobat", "AcroRd32" },
                IncludePaths = new() { @"%APPDATA%\Adobe\Acrobat", @"%LOCALAPPDATA%\Adobe\Acrobat", @"%APPDATA%\Adobe\Common" },
                RegistryKeys = new() { @"HKCU\Software\Adobe\Adobe Acrobat" },
                ExcludeGlobs = new() { @"**\Cache\**", @"**\Temp\**" },
                Notes = new() { "This captures Acrobat user settings and profile data only." }
            },
            new AppRule
            {
                Id = "vlc",
                FriendlyName = "VLC media player",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.79,
                WingetId = "VideoLAN.VLC",
                MatchDisplayNames = new() { "VLC media player", "VLC" },
                MatchPublisherTokens = new() { "VideoLAN" },
                MatchInstallPathTokens = new() { @"VideoLAN\VLC" },
                ProcessNames = new() { "vlc" },
                IncludePaths = new() { @"%APPDATA%\vlc" },
                RegistryKeys = new() { @"HKCU\Software\VideoLAN\VLC" },
                Notes = new() { "Playback history and personal settings are captured." }
            }
        };
    }

    private static AppRule ChromiumRule(string id, string friendlyName, string wingetId, List<string> displayNames, List<string> publisherTokens, List<string> installTokens, List<string> processNames, string dataPath, string registryKey, string notes)
        => new()
        {
            Id = id,
            FriendlyName = friendlyName,
            Category = "Profile-based",
            RestoreStrategy = "reinstall_then_restore_profile",
            Supported = true,
            Confidence = 0.94,
            WingetId = wingetId,
            MatchDisplayNames = displayNames,
            MatchPublisherTokens = publisherTokens,
            MatchInstallPathTokens = installTokens,
            ProcessNames = processNames,
            IncludePaths = new() { dataPath },
            RegistryKeys = new() { registryKey },
            ExcludeGlobs = ChromiumExcludes(),
            Notes = new() { notes }
        };

    private static List<string> ChromiumExcludes()
        => new() { @"**\Cache\**", @"**\Code Cache\**", @"**\Crashpad\**", @"**\GPUCache\**", @"**\Service Worker\CacheStorage\**", @"**\ShaderCache\**" };
}
