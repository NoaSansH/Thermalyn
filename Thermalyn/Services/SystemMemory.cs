// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using Thermalyn.Models;

namespace Thermalyn.Services;

internal static class SystemMemory
{
    public static void Describe(ComponentReading memory, bool includeCommitDetails)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) return;

        var total = status.TotalPhysical / 1073741824d;
        var available = status.AvailablePhysical / 1073741824d;
        memory.Name = LocalizationService.Format("Hardware.UsableMemory", $"{total:0.#}");
        if (total > 0) memory.Load = (total - available) / total * 100;
        Add(memory, "Hardware.PhysicalTotal", total);
        Add(memory, "Hardware.PhysicalUsed", total - available);
        Add(memory, "Hardware.PhysicalAvailable", available);
        if (!includeCommitDetails) return;
        var commitLimit = status.TotalPageFile / 1073741824d;
        var commitAvailable = status.AvailablePageFile / 1073741824d;
        Add(memory, "Hardware.CommittedMemory", commitLimit - commitAvailable);
        Add(memory, "Hardware.CommitLimit", commitLimit);
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
