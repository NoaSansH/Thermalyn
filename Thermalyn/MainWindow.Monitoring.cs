using System.Windows;
using System.Windows.Controls;
using Thermalyn.Models;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    private readonly ThermalAlertService _alerts = new();
    private TrayService? _tray;
    private bool _loadingSettings;
    private bool _isClosed;
    private WindowState _restoreState = WindowState.Normal;
    private ThermalComponent _detailComponent;

    private ThermalComponent ComponentKind(ComponentReading reading) => ReferenceEquals(reading, _snapshot.Cpu)
        || reading.Kind == "CPU" ? ThermalComponent.Cpu
        : ReferenceEquals(reading, _snapshot.Gpu) || _snapshot.Gpus.Contains(reading) || reading.Kind == "GPU"
            ? ThermalComponent.Gpu : ThermalComponent.Storage;

    private IEnumerable<(ComboBox Hot, ComboBox Critical)> ThresholdPairs()
    {
        yield return (HotThresholdCombo, CriticalThresholdCombo);
        yield return (GpuHotThresholdCombo, GpuCriticalThresholdCombo);
        yield return (StorageHotThresholdCombo, StorageCriticalThresholdCombo);
    }

    private void LoadMonitoringControls()
    {
        FillThresholdCombo(GpuHotThresholdCombo, HotThresholds, _settings.GpuHotTemperature);
        FillThresholdCombo(GpuCriticalThresholdCombo, CriticalThresholds, _settings.GpuCriticalTemperature);
        FillThresholdCombo(StorageHotThresholdCombo, HotThresholds, _settings.StorageHotTemperature);
        FillThresholdCombo(StorageCriticalThresholdCombo, CriticalThresholds, _settings.StorageCriticalTemperature);
        MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
        ThermalNotificationsCheck.IsChecked = _settings.ThermalNotifications;
        FillDurationCombo(AlertDelayCombo, [15, 30, 60], _settings.AlertDelaySeconds, "Monitor.Seconds");
        FillDurationCombo(AlertCooldownCombo, [5, 10, 30], _settings.AlertCooldownMinutes, "Monitor.Minutes");
    }

    private static void FillDurationCombo(ComboBox combo, int[] values, int selected, string resource)
    {
        combo.Items.Clear();
        foreach (var value in values)
            combo.Items.Add(new ComboBoxItem { Content = LocalizationService.Format(resource, value), Tag = value });
        combo.SelectedIndex = Array.IndexOf(values, selected);
    }

    private void SaveMonitoringControls()
    {
        _settings.GpuHotTemperature = ThresholdValue(GpuHotThresholdCombo, 80);
        _settings.GpuCriticalTemperature = ThresholdValue(GpuCriticalThresholdCombo, 95);
        _settings.StorageHotTemperature = ThresholdValue(StorageHotThresholdCombo, 60);
        _settings.StorageCriticalTemperature = ThresholdValue(StorageCriticalThresholdCombo, 70);
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        _settings.ThermalNotifications = ThermalNotificationsCheck.IsChecked == true;
        _settings.AlertDelaySeconds = ThresholdValue(AlertDelayCombo, 30);
        _settings.AlertCooldownMinutes = ThresholdValue(AlertCooldownCombo, 10);
        _alerts.Reset();
    }

    private void MonitoringThreshold_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSettings && IsLoaded) ValidateThresholds();
    }

    private void TemperatureUnit_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || !IsLoaded) return;
        _loadingSettings = true;
        foreach (var pair in ThresholdPairs())
        {
            var hot = ThresholdValue(pair.Hot, 80); var critical = ThresholdValue(pair.Critical, 95);
            FillThresholdCombo(pair.Hot, HotThresholds, hot);
            FillThresholdCombo(pair.Critical, CriticalThresholds, critical);
        }
        _loadingSettings = false;
        ValidateThresholds();
    }

    private void NotificationToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (NotificationOptions is not null)
            NotificationOptions.Visibility = ThermalNotificationsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool UpdateTray()
    {
        if (_previewMode || _isClosed) return false;
        if (!_settings.MinimizeToTray && !_settings.ThermalNotifications)
        {
            _tray?.Dispose(); _tray = null;
            return false;
        }
        try
        {
            _tray ??= new TrayService(this, RestoreFromTray, Close);
            var available = _tray.Update($"Thermalyn\nCPU {Temperature(_snapshot.Cpu.Temperature)} · GPU {Temperature(_snapshot.Gpu.Temperature)}");
            if (!available && !IsVisible) RestoreFromTray();
            return available;
        }
        catch (Exception exception)
        {
            Log($"Notification area: {exception.Message}");
            return false;
        }
    }

    private void MinimizeToNotificationArea()
    {
        if (WindowState != WindowState.Minimized) { _restoreState = WindowState; return; }
        // Never hide the only way back to the application if Explorer cannot accept the icon.
        if (_settings.MinimizeToTray && UpdateTray()) Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = _restoreState;
        Activate();
    }

    private void CheckThermalNotifications()
    {
        if (!_settings.ThermalNotifications || _snapshot.IsPartial) { _alerts.Reset(); return; }
        var now = DateTimeOffset.UtcNow;
        Check(_snapshot.Cpu, "CPU", LocalizationService.Get("Common.Processor"));
        Check(_snapshot.Gpu, "GPU:" + _snapshot.Gpu.Name, LocalizationService.Get("Head.GraphicsCard"));
        foreach (var drive in _snapshot.Storage) Check(drive, "Storage:" + drive.Kind + ":" + drive.Name, drive.Name);

        void Check(ComponentReading reading, string id, string name)
        {
            var thresholds = ThermalThresholds.For(_settings, ComponentKind(reading));
            if (_alerts.Track(id, reading.Temperature, thresholds, now, _settings.AlertDelaySeconds,
                    _settings.AlertCooldownMinutes, _settings.RefreshSeconds))
                _tray?.Notify(LocalizationService.Get("Monitor.AlertTitle"),
                    LocalizationService.Format("Monitor.AlertBody", name, Temperature(reading.Temperature),
                        Temperature(thresholds.Hot), _settings.AlertDelaySeconds));
        }
    }
}
