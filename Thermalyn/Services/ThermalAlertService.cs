using Thermalyn.Models;

namespace Thermalyn.Services;

public enum ThermalComponent { Cpu, Gpu, Memory, Storage }

public readonly record struct ThermalThresholds(int Hot, int Critical)
{
    public static ThermalThresholds For(AppSettings settings, ThermalComponent component) => component switch
    {
        ThermalComponent.Gpu => new(settings.GpuHotTemperature, settings.GpuCriticalTemperature),
        ThermalComponent.Storage => new(settings.StorageHotTemperature, settings.StorageCriticalTemperature),
        // DDR5 throttles around 85 C. A specification, not a preference, so it has no setting.
        ThermalComponent.Memory => new(70, 85),
        _ => new(settings.HotTemperature, settings.CriticalTemperature)
    };
}

public sealed class ThermalAlertService
{
    private sealed class Episode
    {
        public DateTimeOffset Started { get; init; }
        public DateTimeOffset LastSample { get; set; }
        public bool Notified { get; set; }
    }

    private readonly Dictionary<string, Episode> _episodes = [];
    private DateTimeOffset? _lastNotification;

    public void Reset()
    {
        _episodes.Clear();
        _lastNotification = null;
    }

    public bool Track(string id, double? temperature, ThermalThresholds thresholds, DateTimeOffset now,
        int delaySeconds, int cooldownMinutes, int refreshSeconds)
    {
        if (temperature is not double value || !double.IsFinite(value))
        {
            _episodes.Remove(id);
            return false;
        }
        if (_episodes.TryGetValue(id, out var episode) &&
            (now < episode.LastSample || now - episode.LastSample > TimeSpan.FromSeconds(Math.Max(10, refreshSeconds * 3))))
        {
            _episodes.Remove(id);
            episode = null;
        }
        if (value < thresholds.Hot - 2)
        {
            _episodes.Remove(id);
            return false;
        }
        if (value < thresholds.Hot)
        {
            if (episode is { Notified: false }) _episodes.Remove(id);
            else if (episode is not null) episode.LastSample = now;
            return false;
        }
        if (episode is null)
            _episodes[id] = episode = new Episode { Started = now };
        episode.LastSample = now;
        if (episode.Notified || now - episode.Started < TimeSpan.FromSeconds(delaySeconds) ||
            _lastNotification is { } last && now - last < TimeSpan.FromMinutes(cooldownMinutes)) return false;
        episode.Notified = true;
        _lastNotification = now;
        return true;
    }
}
