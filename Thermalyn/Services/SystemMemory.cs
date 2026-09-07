using System.Runtime.InteropServices;
using Thermalyn.Models;

namespace Thermalyn.Services;

// Installed and available memory, straight from the kernel. Both the quick read and the full
// sensor pass need it, and neither depends on a driver.
internal static class SystemMemory
{
    public static void Describe(ComponentReading memory, bool includePageFile)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) return;

        var total = status.TotalPhysical / 1073741824d;
        var available = status.AvailablePhysical / 1073741824d;
        memory.Name = LocalizationService.Format("Hardware.InstalledMemory", $"{total:0.#}");
        memory.Load ??= total > 0 ? (total - available) / total * 100 : null;
        Add(memory, "Hardware.PhysicalTotal", total);
        Add(memory, "Hardware.PhysicalUsed", total - available);
        Add(memory, "Hardware.PhysicalAvailable", available);
        if (!includePageFile) return;
        Add(memory, "Hardware.VirtualTotal", status.TotalPageFile / 1073741824d);
        Add(memory, "Hardware.VirtualAvailable", status.AvailablePageFile / 1073741824d);
    }

    private static void Add(ComponentReading memory, string key, double gigabytes) =>
        memory.Details.Add(new SensorReading(LocalizationService.Get(key), "Data", gigabytes, "GB"));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical;
        public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
