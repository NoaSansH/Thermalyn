namespace Thermalyn.Models;

public sealed class ComponentReading
{
    public string Kind { get; init; } = "";
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

public sealed class BatteryReading
{
    public string Name { get; set; } = "";
    public double? ChargePercent { get; set; }
    public double? HealthPercent { get; set; }
    public double? RateWatts { get; set; }
    public bool IsCharging { get; set; }
    public double? Voltage { get; set; }
    public double? DesignedWattHours { get; set; }
    public double? FullChargeWattHours { get; set; }
    public double? RemainingWattHours { get; set; }
    public TimeSpan? RemainingTime { get; set; }
    public List<SensorReading> Details { get; } = [];
}

public sealed class HardwareSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public ComponentReading Cpu { get; init; } = new() { Kind = "CPU" };
    public ComponentReading Gpu { get; init; } = new() { Kind = "GPU" };
    public List<ComponentReading> Gpus { get; init; } = [];
    public ComponentReading Memory { get; init; } = new() { Kind = "RAM" };
    public List<ComponentReading> Storage { get; init; } = [];
    public List<ComponentReading> Fans { get; init; } = [];
    public List<BatteryReading> Batteries { get; init; } = [];
    public string? Error { get; init; }
    public bool LowLevelDriverInstalled { get; init; }
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
