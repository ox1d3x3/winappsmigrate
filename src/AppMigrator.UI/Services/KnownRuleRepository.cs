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

        var best = _rules
            .Select(rule => new { Rule = rule, Score = ScoreRule(rule, displayName, publisher, installLocation) })
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
        foreach (var token in rule.MatchTokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(displayName) && displayName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 1.00;
            }

            if (!string.IsNullOrWhiteSpace(publisher) && publisher.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.70;
            }

            if (!string.IsNullOrWhiteSpace(installLocation) && installLocation.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.45;
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
                // Ignore malformed external rule packs so the app still launches.
            }
        }

        return loadedRules;
    }

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
                Confidence = 0.95,
                WingetId = "Google.Chrome",
                MatchTokens = new() { "Google Chrome", "Chrome", "Google\\Chrome" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\Google\Chrome\User Data"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Google\Chrome"
                },
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new()
                {
                    "Profile restore works best after the same browser is installed on the new machine."
                }
            },
            new AppRule
            {
                Id = "microsoft.edge",
                FriendlyName = "Microsoft Edge",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.95,
                WingetId = "Microsoft.Edge",
                MatchTokens = new() { "Microsoft Edge", "Microsoft\\Edge" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\Microsoft\Edge\User Data"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Microsoft\Edge"
                },
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new()
                {
                    "Edge is usually already present, but a matching build is still safer before restore."
                }
            },
            new AppRule
            {
                Id = "brave.browser",
                FriendlyName = "Brave Browser",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.94,
                WingetId = "Brave.Brave",
                MatchTokens = new() { "Brave", "Brave Browser", "BraveSoftware", "Brave-Browser" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\BraveSoftware\Brave-Browser"
                },
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new()
                {
                    "Brave is now treated like a Chromium profile migration, not as an unsupported app."
                }
            },
            new AppRule
            {
                Id = "vivaldi.browser",
                FriendlyName = "Vivaldi",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.91,
                MatchTokens = new() { "Vivaldi" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\Vivaldi\User Data"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Vivaldi"
                },
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new()
                {
                    "Vivaldi profile migration follows the same pattern as other Chromium browsers."
                }
            },
            new AppRule
            {
                Id = "opera.browser",
                FriendlyName = "Opera",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.90,
                MatchTokens = new() { "Opera GX", "Opera Stable", "Opera" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Opera Software\Opera Stable",
                    @"%APPDATA%\Opera Software\Opera GX Stable"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Opera Software"
                },
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new()
                {
                    "Opera family profile paths are backed up, but reinstall first is still the safe path."
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
                MatchTokens = new() { "Mozilla Firefox", "Firefox", "Mozilla" },
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
                MatchTokens = new() { "Microsoft Visual Studio Code", "Visual Studio Code", "VS Code", "Code" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Code\User",
                    @"%USERPROFILE%\.vscode\extensions",
                    @"%APPDATA%\Code - Insiders\User",
                    @"%USERPROFILE%\.vscode-insiders\extensions"
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
                    "Extensions and user settings restore best into an already installed VS Code environment."
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
                    "Vault content outside the profile folder is still your responsibility to back up separately."
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
                    "Site manager data may contain sensitive server details and credentials."
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
                MatchTokens = new() { "PuTTY", "Simon Tatham" },
                IncludePaths = new(),
                RegistryKeys = new()
                {
                    @"HKCU\Software\SimonTatham\PuTTY"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "PuTTY stores most settings in the HKCU registry."
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
                MatchTokens = new() { "Windows Terminal", "Microsoft.WindowsTerminal" },
                IncludePaths = new()
                {
                    @"%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState"
                },
                RegistryKeys = new(),
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "This restores profile data only, not the whole Store app package."
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
                MatchTokens = new() { "Git version", "Git for Windows", "The Git Development Community", "Git" },
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
            },
            new AppRule
            {
                Id = "sevenzip",
                FriendlyName = "7-Zip",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.84,
                MatchTokens = new() { "7-Zip", "Igor Pavlov" },
                IncludePaths = new()
                {
                    @"%APPDATA%\7-Zip"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\7-Zip"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "This focuses on user settings, not shell extension registration."
                }
            },
            new AppRule
            {
                Id = "greenshot",
                FriendlyName = "Greenshot",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.83,
                MatchTokens = new() { "Greenshot" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Greenshot"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Greenshot"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "Greenshot settings and destinations are backed up."
                }
            },
            new AppRule
            {
                Id = "everything",
                FriendlyName = "Everything",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_state",
                Supported = true,
                Confidence = 0.80,
                MatchTokens = new() { "Everything", "voidtools" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Everything"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\voidtools\Everything"
                },
                ExcludeGlobs = new(),
                Notes = new()
                {
                    "This restores user preferences, not the indexed database service state."
                }
            },
            new AppRule
            {
                Id = "discord",
                FriendlyName = "Discord",
                Category = "Profile-based",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.78,
                MatchTokens = new() { "Discord" },
                IncludePaths = new()
                {
                    @"%APPDATA%\discord"
                },
                RegistryKeys = new(),
                ExcludeGlobs = ChromiumExcludes(),
                Notes = new()
                {
                    "Discord can be restored as profile data, but sign-in tokens may not survive across machines."
                }
            },
            new AppRule
            {
                Id = "adobe.acrobat",
                FriendlyName = "Adobe Acrobat",
                Category = "Settings-only",
                RestoreStrategy = "reinstall_then_restore_profile",
                Supported = true,
                Confidence = 0.76,
                MatchTokens = new() { "Adobe Acrobat", "Adobe Acrobat Reader", "Adobe", "Acrobat" },
                IncludePaths = new()
                {
                    @"%APPDATA%\Adobe\Acrobat",
                    @"%LOCALAPPDATA%\Adobe\Acrobat",
                    @"%APPDATA%\Adobe\Common"
                },
                RegistryKeys = new()
                {
                    @"HKCU\Software\Adobe\Adobe Acrobat"
                },
                ExcludeGlobs = new()
                {
                    @"**\Cache\**",
                    @"**\Temp\**"
                },
                Notes = new()
                {
                    "This captures Acrobat user settings and profile data only. Full Creative Cloud app migration still needs deeper app-specific rules and reinstall-first restore."
                }
            }
        };
    }

    private static List<string> ChromiumExcludes()
    {
        return new()
        {
            @"**\Cache\**",
            @"**\Code Cache\**",
            @"**\Crashpad\**",
            @"**\GPUCache\**",
            @"**\Service Worker\CacheStorage\**",
            @"**\ShaderCache\**"
        };
    }
}
