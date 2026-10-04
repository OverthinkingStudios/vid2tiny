using System.IO;
using System.Text.Json;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(appData, AppInfo.Name);
        Directory.CreateDirectory(appDir);
        _settingsPath = Path.Combine(appDir, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
        return settings ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        using var stream = File.Create(_settingsPath);
        JsonSerializer.Serialize(stream, settings, JsonOptions);
        System.Diagnostics.Debug.WriteLine($"Updated {_settingsPath}");
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        System.Diagnostics.Debug.WriteLine($"Updated {_settingsPath}");
    }
}
