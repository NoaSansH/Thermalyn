// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using Thermalyn.Models;

internal static class SessionStatisticsChecks
{
    public static void Run()
    {
        var metric = new SessionMetric();
        metric.Add(null);
        metric.Add(double.NaN);
        metric.Add(40);
        metric.Add(50);
        metric.Add(60);
        var summary = metric.Summary ?? throw new InvalidOperationException("Session metric has no summary.");
        if (summary.Count != 3 || summary.Minimum != 40 || summary.Average != 50 || summary.Maximum != 60)
            throw new InvalidOperationException("Session min/average/max calculation is incorrect.");
        metric.Reset();
        if (metric.Summary is not null)
            throw new InvalidOperationException("Reset did not clear the in-memory session metric.");

        var session = new SessionStatistics();
        session.Add(new HardwareSnapshot
        {
            Cpu = new() { Temperature = 72 },
            Gpu = new() { Temperature = 64 },
            Memory = new() { Load = 43 }
        });
        if (session.CpuTemperature.Summary?.Average != 72 ||
            session.GpuTemperature.Summary?.Average != 64 ||
            session.MemoryLoad.Summary?.Average != 43)
            throw new InvalidOperationException("Session statistics did not track the hardware snapshot.");

        Console.WriteLine("In-memory session statistics checks passed.");
    }
}
