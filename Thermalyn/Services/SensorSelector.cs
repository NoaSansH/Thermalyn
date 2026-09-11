// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using LibreHardwareMonitor.Hardware;

namespace Thermalyn.Services;

internal enum ComponentSensorKind { Cpu, Gpu, Memory, Storage, Other }

internal readonly record struct SensorSample(string Name, SensorType Type, double Value);

internal readonly record struct MemoryCandidate(string Name, bool HasLoad);

internal static class SensorSelector
{
    public static SensorSample? Temperature(IReadOnlyList<SensorSample> samples, ComponentSensorKind kind, string hardwareName)
    {
        var values = Valid(samples, SensorType.Temperature, value => value is > 0 and < 125);
        return kind switch
        {
            ComponentSensorKind.Cpu when IsAmd(hardwareName) => Exact(values,
                "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "CPU Package", "Core Max", "Core Average", "CPU Cores"),
            ComponentSensorKind.Cpu => Exact(values,
                "CPU Package", "Core Max", "Core Average", "CPU Cores", "Core (Tctl/Tdie)", "Core (Tdie)"),
            ComponentSensorKind.Gpu => Exact(values, "GPU Core", "GPU Temperature"),
            ComponentSensorKind.Storage => Exact(values, "Temperature", "Drive Temperature", "Composite", "Temperature #1"),
            ComponentSensorKind.Memory => Exact(values, "Temperature"),
            _ => Exact(values, "Temperature")
        };
    }

    public static SensorSample? Load(IReadOnlyList<SensorSample> samples, ComponentSensorKind kind)
    {
        var values = Valid(samples, SensorType.Load, value => value is >= 0 and <= 100);
        if (kind == ComponentSensorKind.Cpu)
            return Exact(values, "CPU Total") ?? Average(values, ["CPU Core #", "P-Core #", "E-Core #"], false);
        if (kind == ComponentSensorKind.Gpu)
            return Exact(values, "GPU Core", "GPU Render/Compute") ?? MaximumGpuEngine(values);
        if (kind == ComponentSensorKind.Memory)
            return Exact(values, "Memory");
        if (kind == ComponentSensorKind.Storage)
            return Exact(values, "Total Activity");
        return null;
    }

    public static SensorSample? Clock(IReadOnlyList<SensorSample> samples, ComponentSensorKind kind, string hardwareName)
    {
        var values = Valid(samples, SensorType.Clock, value => value is > 0 and <= 10000);
        if (kind == ComponentSensorKind.Cpu)
        {
            if (IsAmd(hardwareName))
                return Exact(values, "Cores (Average Effective)", "Cores (Average)")
                    ?? Average(values, ["Core #"], true)
                    ?? Average(values, ["Core #"], false);

            return Exact(values, "Cores (Average Effective)", "Cores (Average)", "Core Average")
                ?? Average(values, ["P-Core #", "E-Core #", "Core #"], false);
        }

        return kind == ComponentSensorKind.Gpu ? Exact(values, "GPU Core") : null;
    }

    public static SensorSample? Power(IReadOnlyList<SensorSample> samples, ComponentSensorKind kind, HardwareType hardwareType)
    {
        var values = Valid(samples, SensorType.Power, value => value is >= 0 and < 2000);
        if (kind == ComponentSensorKind.Cpu)
            return Exact(values, "CPU Package", "Package", "Package Power", "CPU PPT", "PPT", "CPU Power");

        if (kind != ComponentSensorKind.Gpu) return null;

        // On an AMD APU "GPU Core" is a shared SMU rail that follows processor load.
        return hardwareType switch
        {
            HardwareType.GpuIntel => Exact(values, "GPU Total", "GPU Package", "GPU Power"),
            HardwareType.GpuAmd => Exact(values, "GPU Package", "GPU PPT"),
            HardwareType.GpuNvidia => Exact(values, "GPU Package", "GPU Board Power"),
            _ => Exact(values, "GPU Package", "GPU Total", "GPU Power", "GPU PPT")
        };
    }

    // "Virtual Memory" is the commit charge. Windows keeps it pinned near its limit.
    public static int PhysicalMemory(IReadOnlyList<MemoryCandidate> candidates)
    {
        var index = IndexOf(candidates, candidate => candidate.Name.Equals("Total Memory", StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = IndexOf(candidates, candidate => candidate.Name.Equals("Generic Memory", StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = IndexOf(candidates, candidate => candidate.HasLoad && !IsVirtual(candidate.Name));
        return index;
    }

    // An SPD block types its warning limits as temperatures too, one within a degree of the reading.
    public static SensorSample? ModuleTemperature(IReadOnlyList<SensorSample> samples)
    {
        foreach (var sample in Valid(samples, SensorType.Temperature, value => value is > 0 and < 125))
            if (IsDimmTemperatureName(sample.Name)) return sample;
        return null;
    }

    private static bool IsDimmTemperatureName(string name) =>
        name.StartsWith("DIMM #", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(name.AsSpan("DIMM #".Length), out _);

    private static bool IsVirtual(string name) => name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<MemoryCandidate> candidates, Func<MemoryCandidate, bool> match)
    {
        for (var i = 0; i < candidates.Count; i++)
            if (match(candidates[i])) return i;
        return -1;
    }

    public static SensorSample? Fan(IReadOnlyList<SensorSample> samples)
    {
        var values = Valid(samples, SensorType.Fan, value => value is >= 0 and < 100000);
        return Exact(values, "GPU Fan", "GPU") ?? (values.Count > 0 ? values[0] : null);
    }

    // A battery publishes "Charge Rate" and "Charge Current" while it charges and the "Discharge"
    // pair while it drains, never both, and "Remaining Time (Estimated)" disappears on charge.
    public static SensorSample? BatteryLevel(IReadOnlyList<SensorSample> samples, string name) =>
        Exact(Valid(samples, SensorType.Level, value => value is >= 0 and <= 100), name);

    public static SensorSample? BatteryVoltage(IReadOnlyList<SensorSample> samples) =>
        Exact(Valid(samples, SensorType.Voltage, value => value is > 0 and < 100), "Voltage");

    public static SensorSample? BatteryRate(IReadOnlyList<SensorSample> samples, bool charging) =>
        Exact(Valid(samples, SensorType.Power, value => value is >= 0 and < 500),
            charging ? "Charge Rate" : "Discharge Rate");

    public static SensorSample? BatteryCapacity(IReadOnlyList<SensorSample> samples, string name) =>
        Exact(Valid(samples, SensorType.Energy, value => value > 0), name);

    public static SensorSample? BatteryRemainingTime(IReadOnlyList<SensorSample> samples) =>
        Exact(Valid(samples, SensorType.TimeSpan, value => value is > 0 and < 172800), "Remaining Time (Estimated)");

    public static bool BatteryIsCharging(IReadOnlyList<SensorSample> samples) =>
        Exact(Valid(samples, SensorType.Power, value => value >= 0), "Charge Rate") is not null ||
        Exact(Valid(samples, SensorType.Current, value => value >= 0), "Charge Current") is not null;

    private static List<SensorSample> Valid(IEnumerable<SensorSample> samples, SensorType type, Func<double, bool> predicate) =>
        samples.Where(sample => sample.Type == type && double.IsFinite(sample.Value) && predicate(sample.Value)).ToList();

    private static SensorSample? Exact(IReadOnlyList<SensorSample> samples, params string[] names)
    {
        foreach (var name in names)
            foreach (var sample in samples)
                if (sample.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return sample;
        return null;
    }

    private static SensorSample? Average(IReadOnlyList<SensorSample> samples, string[] prefixes, bool effectiveOnly)
    {
        var matches = samples.Where(sample => prefixes.Any(prefix => sample.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Where(sample => !sample.Name.Contains("Max", StringComparison.OrdinalIgnoreCase))
            .Where(sample => !effectiveOnly || sample.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 0 ? null : new SensorSample("Average", matches[0].Type, matches.Average(sample => sample.Value));
    }

    private static SensorSample? MaximumGpuEngine(IReadOnlyList<SensorSample> samples)
    {
        // Memory, bus and power-limit percentages are typed Load by the library as well.
        var engines = samples.Where(sample =>
                sample.Name.StartsWith("D3D ", StringComparison.OrdinalIgnoreCase) &&
                !sample.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) &&
                !sample.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase) &&
                !sample.Name.Contains("Power", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return engines.Count == 0 ? null : engines.MaxBy(sample => sample.Value);
    }

    private static bool IsAmd(string name) =>
        name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
}
