// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Thermalyn.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 10;

    public string Language { get; set; } = "";
    public string Theme { get; set; } = "Dark";
    public string ViewMode { get; set; } = "Balanced";
    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool ShowAnimations { get; set; } = true;
    public bool ShowFps { get; set; }
    public bool ShowGraphs { get; set; } = true;
    // Split into the two below in version 5; still read to migrate older files.
    public string PrimaryMetric { get; set; } = "Temperature";
    public string CpuPrimaryMetric { get; set; } = "Temperature";
    public string GpuPrimaryMetric { get; set; } = "Temperature";
    public bool GpuFirst { get; set; }
    public bool ShowFans { get; set; } = true;
    public bool ShowProcesses { get; set; } = true;
    public string TemperatureUnit { get; set; } = "C";
    public int MaxDrives { get; set; } = 4;
    public string AccentColor { get; set; } = "#3B9EFF";
    public string NormalTemperatureColor { get; set; } = "#3B9EFF";
    public string HotTemperatureColor { get; set; } = "#FFA83B";
    public string CriticalTemperatureColor { get; set; } = "#FF5C5C";
    public int HotTemperature { get; set; } = 80;
    public int CriticalTemperature { get; set; } = 95;
    public int GpuHotTemperature { get; set; } = 80;
    public int GpuCriticalTemperature { get; set; } = 95;
    public int StorageHotTemperature { get; set; } = 60;
    public int StorageCriticalTemperature { get; set; } = 70;
    public bool MinimizeToTray { get; set; }
    public bool ThermalNotifications { get; set; }
    public int AlertDelaySeconds { get; set; } = 30;
    public int AlertCooldownMinutes { get; set; } = 10;
    public int RefreshSeconds { get; set; } = 1;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 820;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
}
