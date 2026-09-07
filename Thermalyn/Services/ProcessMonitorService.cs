using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Thermalyn.Models;

namespace Thermalyn.Services;

public sealed partial class ProcessMonitorService
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Time)> _previous = [];
    private readonly Dictionary<string, (long Busy, DateTime Time)> _gpuPrevious = [];
    private PerformanceCounterCategory? _gpuEngine;

    public List<ProcessUsage> Read()
    {
        var now = DateTime.UtcNow;
        var gpu = ReadGpuUsage(now);
        var result = new List<ProcessUsage>();
        var alive = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var id = process.Id;
                alive.Add(id);
                var cpuTime = process.TotalProcessorTime;
                var cpu = 0d;
                if (_previous.TryGetValue(id, out var prior))
                {
                    var elapsed = (now - prior.Time).TotalMilliseconds;
                    if (elapsed > 0) cpu = Math.Clamp((cpuTime - prior.Cpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
                }
                _previous[id] = (cpuTime, now);
                result.Add(new ProcessUsage(id, FriendlyName(process.ProcessName), cpu, process.WorkingSet64 / 1048576d,
                    Math.Clamp(gpu.GetValueOrDefault(id), 0, 100)));
            }
            catch { }
            finally { process.Dispose(); }
        }
        foreach (var stale in _previous.Keys.Where(id => !alive.Contains(id)).ToList()) _previous.Remove(stale);
        return result;
    }

    // Per-process graphics use. Windows publishes hundreds of "GPU Engine" instances, so read the
    // category in one call. Raw values are busy time in 100 ns units.
    private Dictionary<int, double> ReadGpuUsage(DateTime now)
    {
        var result = new Dictionary<int, double>();
        try
        {
            _gpuEngine ??= new PerformanceCounterCategory("GPU Engine");
            var samples = _gpuEngine.ReadCategory()["Utilization Percentage"];
            var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry entry in samples)
            {
                var instance = (string)entry.Key;
                alive.Add(instance);
                if (entry.Value is not InstanceData sample) continue;
                var busy = sample.RawValue;

                if (_gpuPrevious.TryGetValue(instance, out var prior))
                {
                    var elapsed = (now - prior.Time).TotalSeconds;
                    // A counter that went backwards means the engine instance was recycled.
                    if (elapsed > 0 && busy >= prior.Busy)
                    {
                        var percent = (busy - prior.Busy) / 1e7 / elapsed * 100;
                        if (percent > .05)
                        {
                            var match = GpuPidRegex().Match(instance);
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
                                result[pid] = result.GetValueOrDefault(pid) + percent;
                        }
                    }
                }
                _gpuPrevious[instance] = (busy, now);
            }

            foreach (var stale in _gpuPrevious.Keys.Where(key => !alive.Contains(key)).ToList()) _gpuPrevious.Remove(stale);
        }
        catch
        {
            // Absent without a WDDM 2.0 driver.
            _gpuEngine = null;
        }
        return result;
    }

    private static string FriendlyName(string value) => value switch
    {
        "explorer" => LocalizationService.Get("Process.Explorer"),
        "SearchHost" => LocalizationService.Get("Process.Search"),
        "ApplicationFrameHost" => LocalizationService.Get("Process.AppFrame"),
        _ => value
    };

    [GeneratedRegex(@"pid_(\d+)_", RegexOptions.IgnoreCase)]
    private static partial Regex GpuPidRegex();
}
