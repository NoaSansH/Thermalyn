using System.Management;
using System.Runtime.InteropServices;
using Thermalyn.Models;

namespace Thermalyn.Services;

// What Windows reports without a driver. Shown until the sensor stack finishes opening.
public static class QuickReadService
{
    public static HardwareSnapshot Read()
    {
        var cpu = new ComponentReading { Kind = "CPU" };
        var gpu = new ComponentReading { Kind = "GPU" };
        var memory = new ComponentReading { Kind = "RAM" };

        ReadProcessor(cpu);
        ReadGraphics(gpu);
        ReadMemory(memory);
        // No ACPI thermal zone: it measures the chassis, not the processor.

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

    private static void ReadMemory(ComponentReading memory)
    {
        try
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref status)) return;
            var total = status.TotalPhysical / 1073741824d;
            var available = status.AvailablePhysical / 1073741824d;
            var used = total - available;
            memory.Name = LocalizationService.Format("Hardware.InstalledMemory", $"{total:0.#}");
            if (total > 0) memory.Load = used / total * 100;
            memory.Details.Add(new SensorReading(LocalizationService.Get("Hardware.PhysicalTotal"), "Data", total, "GB"));
            memory.Details.Add(new SensorReading(LocalizationService.Get("Hardware.PhysicalUsed"), "Data", used, "GB"));
            memory.Details.Add(new SensorReading(LocalizationService.Get("Hardware.PhysicalAvailable"), "Data", available, "GB"));
        }
        catch { }
    }

    private static double? TryDouble(object? value) => double.TryParse(value?.ToString(), out var parsed) ? parsed : null;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical;
        public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
