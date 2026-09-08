using System.IO;
using System.Text.Json;
using Thermalyn.Models;

namespace Thermalyn.Services;

public sealed class SettingsService
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thermalyn");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
                using var document = JsonDocument.Parse(json);
                var legacy = !document.RootElement.TryGetProperty(nameof(AppSettings.SettingsVersion), out _);
                var previousVersion = legacy ? 0 : settings.SettingsVersion;
                if (previousVersion < 4)
                {
                    settings.WindowWidth = 1200;
                    settings.WindowHeight = 820;
                    settings.WindowLeft = null;
                    settings.WindowTop = null;
                }
                if (previousVersion < 5)
                {
                    settings.CpuPrimaryMetric = settings.PrimaryMetric;
                    settings.GpuPrimaryMetric = settings.PrimaryMetric;
                }
                if (previousVersion < 6)
                {
                    settings.AccentColor = "#3B9EFF";
                    settings.NormalTemperatureColor = "#3B9EFF";
                    settings.HotTemperatureColor = "#FFA83B";
                    settings.CriticalTemperatureColor = "#FF5C5C";
                }
                if (previousVersion < 9)
                {
                    settings.HotTemperature = 80;
                    settings.CriticalTemperature = 95;
                }
                MigrateMonitoring(settings, previousVersion);
                return settings;
            }
        }
        catch { }
        return new();
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, WriteOptions));
    }

    internal static void MigrateMonitoring(AppSettings settings, int previousVersion)
    {
        if (previousVersion < 10)
        {
            settings.GpuHotTemperature = settings.StorageHotTemperature = settings.HotTemperature;
            settings.GpuCriticalTemperature = settings.StorageCriticalTemperature = settings.CriticalTemperature;
        }
        settings.HotTemperature = Math.Clamp(settings.HotTemperature, 40, 100);
        settings.CriticalTemperature = Math.Clamp(settings.CriticalTemperature, settings.HotTemperature + 5, 110);
        settings.GpuHotTemperature = Math.Clamp(settings.GpuHotTemperature, 40, 100);
        settings.GpuCriticalTemperature = Math.Clamp(settings.GpuCriticalTemperature, settings.GpuHotTemperature + 5, 110);
        settings.StorageHotTemperature = Math.Clamp(settings.StorageHotTemperature, 40, 100);
        settings.StorageCriticalTemperature = Math.Clamp(settings.StorageCriticalTemperature, settings.StorageHotTemperature + 5, 110);
        settings.AlertDelaySeconds = settings.AlertDelaySeconds is 15 or 30 or 60 ? settings.AlertDelaySeconds : 30;
        settings.AlertCooldownMinutes = settings.AlertCooldownMinutes is 5 or 10 or 30 ? settings.AlertCooldownMinutes : 10;
        settings.SettingsVersion = 10;
    }
}
