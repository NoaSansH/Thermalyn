// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Thermalyn.Models;

public readonly record struct SessionSummary(int Count, double Minimum, double Average, double Maximum);

public sealed class SessionMetric
{
    private int _count;
    private double _sum;
    private double _minimum = double.PositiveInfinity;
    private double _maximum = double.NegativeInfinity;

    public void Add(double? value)
    {
        if (value is not double number || !double.IsFinite(number)) return;
        _count++;
        _sum += number;
        _minimum = Math.Min(_minimum, number);
        _maximum = Math.Max(_maximum, number);
    }

    public SessionSummary? Summary => _count == 0
        ? null
        : new SessionSummary(_count, _minimum, _sum / _count, _maximum);

    public void Reset()
    {
        _count = 0;
        _sum = 0;
        _minimum = double.PositiveInfinity;
        _maximum = double.NegativeInfinity;
    }
}

public sealed class SessionStatistics
{
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.Now;
    public SessionMetric CpuTemperature { get; } = new();
    public SessionMetric GpuTemperature { get; } = new();
    public SessionMetric MemoryLoad { get; } = new();

    public void Add(HardwareSnapshot snapshot)
    {
        CpuTemperature.Add(snapshot.Cpu.Temperature);
        GpuTemperature.Add(snapshot.Gpu.Temperature);
        MemoryLoad.Add(snapshot.Memory.Load);
    }

    public void Reset()
    {
        StartedAt = DateTimeOffset.Now;
        CpuTemperature.Reset();
        GpuTemperature.Reset();
        MemoryLoad.Reset();
    }
}
