namespace Thermalyn.Models;

public sealed class ComponentReading
{
    public string Kind { get; init; } = "";
    // Empty until the hardware is identified. The UI shows a localized label instead.
    public string Name { get; set; } = "";
    public double? Temperature { get; set; }
    public double? Load { get; set; }
    public double? FanRpm { get; set; }
    public double? ClockMhz { get; set; }
    public double? PowerWatts { get; set; }
    public bool IsIntegratedGpu { get; set; }
    public bool IsActiveGpu { get; set; }
    public List<SensorReading> Details { get; } = [];
}

public sealed record SensorReading(string Name, string Type, double Value, string Unit);

public sealed class HardwareSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public ComponentReading Cpu { get; init; } = new() { Kind = "CPU" };
    public ComponentReading Gpu { get; init; } = new() { Kind = "GPU" };
    public List<ComponentReading> Gpus { get; init; } = [];
    public ComponentReading Memory { get; init; } = new() { Kind = "RAM" };
    public List<ComponentReading> Storage { get; init; } = [];
    public List<ComponentReading> Fans { get; init; } = [];
    public string? Error { get; init; }
    public bool LowLevelDriverInstalled { get; init; }
    // Set on the first-paint snapshot, which only has what Windows reports directly.
    public bool IsPartial { get; init; }
}

public sealed class ThermalEvent
{
    public string Component { get; set; } = "";
    public string Level { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public double Peak { get; set; }
}

public sealed record ProcessUsage(int Id, string Name, double CpuPercent, double MemoryMb, double GpuPercent);
