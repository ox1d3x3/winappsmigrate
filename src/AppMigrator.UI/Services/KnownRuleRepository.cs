using System;
using System.Collections.Generic;
using System.Linq;
using AppMigrator.UI.Models;

namespace AppMigrator.UI.Services;

public sealed class KnownRuleRepository
{
    private readonly List<AppRule> _rules = BuildRules();

    public IReadOnlyList<AppRule> GetRules() => _rules;

    public AppRule? Match(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        return _rules.FirstOrDefault(rule =>
            rule.MatchTokens.Any(token =>
                displayName.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    public AppRule? GetById(string ruleId)
        => _rules.FirstOrDefault(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));

    private static List<AppRule> BuildRules()
    {
        return new()
        {
            new AppRule
            {
                Id = "google.chrome",
                FriendlyName = "Google Chrome",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.94,
                WingetId = "Google.Chrome",
                MatchTokens = new() { "Google Chrome" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\Google\Chrome\User Data"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Google\Chrome"
                },
                ExcludeGlobs = new()
                {
                    @"**\Cache\**",
                    @"**\Code Cache\**",
                    @"**\Crashpad\**",
                    @"**\GPUCache\**"
                },
                Notes = new()
                {
                    "Best restored after Chrome is installed on the new machine."
                }
            },
            new AppRule
            {
                Id = "microsoft.edge",
                FriendlyName = "Microsoft Edge",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.94,
                WingetId = "Microsoft.Edge",
                MatchTokens = new() { "Microsoft Edge" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\Microsoft\Edge\User Data"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Microsoft\Edge"
                },
                ExcludeGlobs = new()
                {
                    @"**\Cache\**",
                    @"**\Code Cache\**",
                    @"**\Crashpad\**",
                    @"**\GPUCache\**"
                },
                Notes = new()
                {
                    "Edge is usually present on Windows already, but restoring into a matching version is safer."
                }
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
                MatchTokens = new() { "Mozilla Firefox", "Firefox" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Mozilla\Firefox",
                    @"%LOCALAPPDATA%\Mozilla\Firefox"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Mozilla"
                },
                ExcludeGlobs = new()
                {
                    @"**\cache2\**",
                    @"**\startupCache\**"
                },
                Notes = new()
                {
                    "Best restored after Firefox is installed on the new machine."
                }
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
                MatchTokens = new() { "Microsoft Visual Studio Code", "Visual Studio Code", "VS Code" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Code\User",
                    @"%USERPROFILE%\.vscode\extensions"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Microsoft\VSCommon"
                },
                ExcludeGlobs = new()
                {
                    @"**\CachedData\**",
                    @"**\Cache\**"
                },
                Notes = new()
                {
                    "Extensions restore best into an already installed VS Code environment."
                }
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
                MatchTokens = new() { "Obsidian" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Obsidian"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Obsidian"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "Vault content is not backed up automatically unless it lives inside the standard profile paths."
                }
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
                MatchTokens = new() { "Notepad++" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Notepad++"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Notepad++"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "Typical user settings and session state are captured."
                }
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
                MatchTokens = new() { "FileZilla" },
                IncludePaths = new()
                {
                    @"%APPDATA%\FileZilla"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\FileZilla"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "Site manager data may include sensitive server details."
                }
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
                MatchTokens = new() { "PuTTY" },
                IncludePaths = new(),
                RegistryKeys = new()
                {
                    @"HKCU\Software\SimonTatham\PuTTY"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "PuTTY stores most settings in HKCU registry."
                }
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
                MatchTokens = new() { "Windows Terminal" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState"
                },
                RegistryKeys = new(),
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "Store/MSIX packaging means full cloning is not promised. Profile data only."
                }
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
                MatchTokens = new() { "Git", "Git version" },
                IncludePaths = new()
                {
                    @"%USERPROFILE%\.gitconfig",
                    @"%USERPROFILE%\.git-credentials"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\GitForWindows"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "Credentials may contain sensitive information."
                }
            }
        };
    }
}
