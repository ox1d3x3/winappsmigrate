using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AppMigrator.UI.Helpers;

namespace AppMigrator.UI.Services;

public sealed class UserSettingsService
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinAppsMigrator", "settings.json");

    public async Task<UserSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new UserSettings();
            }

            var json = await File.ReadAllTextAsync(SettingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonHelper.DefaultOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public async Task SaveAsync(UserSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonHelper.DefaultOptions);
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}

public sealed class UserSettings
{
    public string Theme { get; set; } = "Light";
}
