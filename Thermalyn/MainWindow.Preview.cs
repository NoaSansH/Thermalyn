// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using Thermalyn.Models;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    internal async Task PreparePreviewAsync(AppSettings settings, string page)
    {
        if (!_previewMode) throw new InvalidOperationException("Preview requires an isolated window.");
        _settings = settings;
        LocalizationService.Apply(settings.Language);
        ApplyTheme(settings.Theme);
        LoadSettingsControls();
        _snapshot = new HardwareSnapshot
        {
            Cpu = new() { Kind = "CPU", Name = "AMD Ryzen 9 7950X 16-Core Processor", Temperature = 72, Load = 38, FanRpm = 1240, ClockMhz = 4850, PowerWatts = 87 },
            Gpu = new() { Kind = "GPU", Name = "NVIDIA GeForce RTX 4080 SUPER", Temperature = 64, Load = 56, FanRpm = 1100, PowerWatts = 185, IsActiveGpu = true },
            Memory = new() { Kind = "RAM", Name = "32 GB DDR5", Load = 43, Temperature = 46 },
            Batteries =
            [
                new()
                {
                    Name = "Internal Battery",
                    ChargePercent = 75.4,
                    HealthPercent = 87.7,
                    RateWatts = 17.8,
                    IsCharging = true,
                    Voltage = 16.4,
                    DesignedWattHours = 52.5,
                    FullChargeWattHours = 46.1,
                    RemainingWattHours = 34.7
                }
            ],
            Storage = [new() { Kind = "SSD 1", Name = "Samsung SSD 990 PRO 2TB", Temperature = 48 }],
            LowLevelDriverInstalled = true
        };
        _snapshot.Gpus.Add(_snapshot.Gpu);
        foreach (var history in new[] { _cpuHistory, _gpuHistory, _ramHistory, _cpuLoadHistory, _gpuLoadHistory })
        {
            history.Clear();
            for (var i = 0; i < 90; i++) history.Enqueue(50 + 8 * Math.Sin(i / 9d));
        }
        _firstRenderDone = false;
        await RenderAsync(false);
        ApplyMode(settings.ViewMode);
        if (page == "Settings") Settings_Click(this, new RoutedEventArgs());
        else if (page == "Details") ShowDetails(_snapshot.Cpu);
        HardwareDriverBanner.Visibility = Visibility.Collapsed;
    }
}
