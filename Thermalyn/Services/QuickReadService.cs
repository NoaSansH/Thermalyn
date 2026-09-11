// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Management;
using Thermalyn.Models;

namespace Thermalyn.Services;

public static class QuickReadService
{
    public static HardwareSnapshot Read()
    {
        var cpu = new ComponentReading { Kind = "CPU" };
        var gpu = new ComponentReading { Kind = "GPU" };
        var memory = new ComponentReading { Kind = "RAM" };

        ReadProcessor(cpu);
        ReadGraphics(gpu);
        SystemMemory.Describe(memory, includeCommitDetails: false);

        return new HardwareSnapshot
        {
            Cpu = cpu,
            Gpu = gpu,
            Gpus = string.IsNullOrEmpty(gpu.Name) ? [] : [gpu],
            Memory = memory,
            IsPartial = true,
            LowLevelDriverInstalled = true
        };
    }

    private static void ReadProcessor(ComponentReading cpu)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, LoadPercentage, CurrentClockSpeed FROM Win32_Processor");
            var processors = searcher.Get().Cast<ManagementObject>().ToList();
            if (processors.Count == 0) return;
            cpu.Name = processors.Select(p => p["Name"]?.ToString()?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? cpu.Name;
            var loads = processors.Select(p => TryDouble(p["LoadPercentage"])).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            var clocks = processors.Select(p => TryDouble(p["CurrentClockSpeed"])).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (loads.Count > 0) cpu.Load = loads.Average();
            if (clocks.Count > 0) cpu.ClockMhz = clocks.Max();
        }
        catch { }
    }

    private static void ReadGraphics(ComponentReading gpu)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_VideoController");
            var name = searcher.Get().Cast<ManagementObject>()
                .Select(item => item["Name"]?.ToString()?.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("Remote", StringComparison.OrdinalIgnoreCase));
            if (name is null) return;
            gpu.Name = name;
            gpu.IsActiveGpu = true;
        }
        catch { }
    }

    private static double? TryDouble(object? value) => double.TryParse(value?.ToString(), out var parsed) ? parsed : null;
}
