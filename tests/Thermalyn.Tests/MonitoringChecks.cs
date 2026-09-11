// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json;
using Thermalyn.Models;
using Thermalyn.Services;

internal static class MonitoringChecks
{
    public static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"SettingsVersion":9,"HotTemperature":77,"CriticalTemperature":91}""")!;
        SettingsService.MigrateMonitoring(settings, 9);
        Check(settings.GpuHotTemperature == 77 && settings.StorageHotTemperature == 77 && settings.StorageCriticalTemperature == 91,
            "Migration must preserve existing custom thresholds for every component.");
        settings.GpuHotTemperature = 85;
        settings.StorageHotTemperature = 60;
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(ThermalThresholds.For(roundTrip, ThermalComponent.Cpu).Hot == 77 &&
            ThermalThresholds.For(roundTrip, ThermalComponent.Gpu).Hot == 85 &&
            ThermalThresholds.For(roundTrip, ThermalComponent.Storage).Hot == 60, "Component thresholds must persist independently.");
        Check(ThermalThresholds.For(roundTrip, ThermalComponent.Memory) == new ThermalThresholds(70, 85),
            "Memory keeps its own band whatever the other thresholds are set to.");
        Check(!new AppSettings().ThermalNotifications && !new AppSettings().MinimizeToTray, "Background behavior must remain opt-in.");

        var alerts = new ThermalAlertService();
        var start = DateTimeOffset.UnixEpoch;
        bool Sample(int seconds, double? value, string id = "cpu") => alerts.Track(id, value, new(80, 95), start.AddSeconds(seconds), 15, 5, 1);
        Check(!Sample(0, 90) && !Sample(5, 90) && !Sample(10, 90) && Sample(15, 90), "Alert requires continuous sustained readings.");
        Check(!Sample(20, 99), "A sustained episode must not spam notifications.");
        Check(!Sample(21, 79) && !Sample(22, 81), "Threshold jitter must not reopen an episode.");
        Sample(23, 70);
        for (var second = 24; second < 315; second++) Check(!Sample(second, 90), "Cooldown must suppress a second episode.");
        Check(Sample(315, 90), "A new sustained episode can notify after cooldown.");
        alerts.Reset();
        Sample(0, 90); Sample(5, 90); Sample(10, null);
        Check(!Sample(15, 90) && !Sample(20, 90), "Missing sensor data must restart the delay.");
        alerts.Reset();
        Sample(0, 90);
        Check(!Sample(40, 90), "A stalled sensor or sleep interval must not count as sustained heat.");
        alerts.Reset();
        Sample(0, 90); Sample(5, 90); Sample(10, 79);
        Check(!Sample(15, 90), "A brief excursion must not accumulate separate hot intervals.");
        alerts.Reset();
        for (var second = 0; second <= 15; second++) Sample(second, 90);
        for (var second = 16; second <= 40; second++) Check(!Sample(second, 90, "gpu"), "Cooldown must also limit simultaneous component alerts.");
        Console.WriteLine("Monitoring: migration, independent thresholds, duration, recovery, missing data and cooldown checks passed.");
    }
}
