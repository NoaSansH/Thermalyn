using System.Diagnostics;
using System.IO;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using System.Management;
using Thermalyn.Models;

namespace Thermalyn.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private Computer _computer = CreateComputer();

    // SPD discovery is synchronous and can take tens of seconds. Memory opens last.
    private static Computer CreateComputer() => new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = false,
        IsMemoryEnabled = false,
        IsStorageEnabled = false,
        IsMotherboardEnabled = false,
        IsControllerEnabled = false,
        IsNetworkEnabled = false,
        IsPsuEnabled = false
    };
    private bool _opened;
    private int _stage;
    private Task? _memoryOpenTask;

    public bool IsFullyOpen => Volatile.Read(ref _stage) >= 3;

    private static readonly TimeSpan InventoryLifetime = TimeSpan.FromMinutes(2);
    private DateTime _inventoryStamp = DateTime.MinValue;
    private List<DiskInventory> _diskInventory = [];
    private List<VideoInventory> _videoInventory = [];
    private List<ComponentReading> _wmiFans = [];
    private string? _cpuName;
    private bool _publishedMonitorMissing;

    private const int SlowGroupInterval = 10;
    private int _readCount;

    private const string PawnIoUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    private static readonly string DiagnosticLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thermalyn", "diagnostic.log");
    private static bool? _tracing;

    private bool InventoryExpired => DateTime.UtcNow - _inventoryStamp > InventoryLifetime;

    private void RefreshInventory()
    {
        if (!InventoryExpired) return;
        _inventoryStamp = DateTime.UtcNow;
        _diskInventory = ReadPhysicalDiskInventory();
        if (_diskInventory.Count == 0) _diskInventory = ReadDiskDriveInventory();
        _videoInventory = ReadVideoInventory();
        _wmiFans = ReadWmiFans();
        _cpuName ??= ReadWmiCpuName();
    }

    private void OpenNextStage()
    {
        var watch = Stopwatch.StartNew();
        switch (_stage)
        {
            case 0:
                _stage = 1;
                try { _computer.IsGpuEnabled = true; } catch { }
                Trace($"GPU group opened in {watch.ElapsedMilliseconds} ms");
                return;
            case 1:
                _stage = 2;
                try
                {
                    _computer.IsMotherboardEnabled = true;
                    _computer.IsStorageEnabled = true;
                    _computer.IsControllerEnabled = true;
                    _computer.IsPsuEnabled = true;
                }
                catch { }
                Trace($"Motherboard, storage and cooling groups opened in {watch.ElapsedMilliseconds} ms");
                return;
            case 2:
                var computer = _computer;
                _memoryOpenTask ??= Task.Run(() =>
                {
                    var memoryWatch = Stopwatch.StartNew();
                    try { computer.IsMemoryEnabled = true; }
                    catch { }
                    finally
                    {
                        Volatile.Write(ref _stage, 3);
                        Trace($"Memory and SPD groups opened in {memoryWatch.ElapsedMilliseconds} ms");
                    }
                });
                return;
        }
    }

    public void Open()
    {
        if (_opened) return;
        var watch = Stopwatch.StartNew();
        _computer.Open();
        _opened = true;
        Trace($"CPU group opened in {watch.ElapsedMilliseconds} ms");
    }

    internal static void Trace(string message)
    {
        if (!_tracing.HasValue)
        {
            try { _tracing = File.Exists(DiagnosticLogPath); }
            catch { _tracing = false; }
        }
        if (_tracing != true) return;

        try { File.AppendAllText(DiagnosticLogPath, $"{DateTime.Now:u}  {message}{Environment.NewLine}"); }
        catch { _tracing = false; }
    }

    public HardwareSnapshot Read()
    {
        try
        {
            var watch = Stopwatch.StartNew();
            Open();
            var openMs = watch.ElapsedMilliseconds;
            var all = new List<IHardware>();
            var refreshSlow = _readCount++ % SlowGroupInterval == 0;
            foreach (var root in _computer.Hardware)
                UpdateHardwareTree(root, all, refreshSlow || !IsSlowGroup(root.HardwareType));
            var updateMs = watch.ElapsedMilliseconds;

            var cpuHardware = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            var gpuHardware = all.Where(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel).ToList();
            var memoryHardware = SelectPhysicalMemory(all);
            var drives = all.Where(h => h.HardwareType == HardwareType.Storage).Take(4).ToList();

            RefreshInventory();
            var cpu = BuildComponent("CPU", cpuHardware, ComponentSensorKind.Cpu);
            if (string.IsNullOrEmpty(cpu.Name) && _cpuName is not null) cpu.Name = _cpuName;
            if (!cpu.Temperature.HasValue)
            {
                var boardTemperature = FindCpuTemperature(all);
                if (boardTemperature is not null)
                {
                    cpu.Temperature = boardTemperature.Value;
                    cpu.Details.Add(new SensorReading(boardTemperature.Name, "Temperature", boardTemperature.Value, "°C"));
                }
            }
            cpu.Temperature ??= ReadPublishedMonitorCpuTemperature();
            if (!cpu.PowerWatts.HasValue)
            {
                var boardPower = FindCpuPower(all);
                if (boardPower is not null)
                {
                    cpu.PowerWatts = boardPower.Value;
                    cpu.Details.Add(new SensorReading(boardPower.Name, "Power", boardPower.Value, "W"));
                }
            }
            cpu.PowerWatts ??= ReadPublishedMonitorCpuPower();
            var gpus = gpuHardware.Select(BuildGpu).ToList();
            MergeWmiGpuInventory(gpus);
            var activeGpu = gpus.OrderByDescending(GpuActivityScore).FirstOrDefault()
                ?? new ComponentReading { Kind = "GPU" };
            activeGpu.IsActiveGpu = true;
            var fans = BuildFans(all);
            if (fans.Count == 0) fans = _wmiFans;

            var memory = BuildComponent("RAM", memoryHardware, ComponentSensorKind.Memory);
            memory.Details.Clear();
            SystemMemory.Describe(memory, includeCommitDetails: true);
            AttachMemoryModules(memory, all);
            var inventory = _diskInventory;
            var storage = drives.Select((d, i) => BuildComponent($"{MediaLabel(MediaTypeFor(d.Name, inventory))} {i + 1}", d, ComponentSensorKind.Storage)).ToList();
            MergeWindowsStorageInventory(storage, inventory);
            var snapshot = new HardwareSnapshot
            {
                Cpu = cpu,
                Gpu = activeGpu,
                Gpus = gpus,
                Memory = memory,
                Storage = storage,
                Fans = fans,
                LowLevelDriverInstalled = IsPawnIoInstalled()
            };
            Trace($"Read: open {openMs} ms, update {updateMs - openMs} ms, rest {watch.ElapsedMilliseconds - updateMs} ms, total {watch.ElapsedMilliseconds} ms");
            return snapshot;
        }
        catch (Exception ex)
        {
            return new HardwareSnapshot { Error = ex.Message };
        }
        finally
        {
            OpenNextStage();
        }
    }

    private static void AttachMemoryModules(ComponentReading memory, List<IHardware> all)
    {
        foreach (var module in all.Where(hardware => hardware.HardwareType == HardwareType.Memory))
        {
            var samples = module.Sensors.Where(sensor => sensor.Value.HasValue)
                .Select(sensor => new SensorSample(sensor.Name, sensor.SensorType, sensor.Value!.Value)).ToList();
            if (SensorSelector.ModuleTemperature(samples) is not { } reading) continue;
            memory.Details.Add(new SensorReading(reading.Name, "Temperature", reading.Value, "°C"));
            memory.Temperature = Math.Max(memory.Temperature ?? reading.Value, reading.Value);
        }
    }

    private static IHardware? SelectPhysicalMemory(List<IHardware> all)
    {
        var candidates = all.Where(hardware => hardware.HardwareType == HardwareType.Memory).ToList();
        var index = SensorSelector.PhysicalMemory(candidates
            .Select(hardware => new MemoryCandidate(hardware.Name, hardware.Sensors.Any(sensor => sensor.SensorType == SensorType.Load)))
            .ToList());
        return index >= 0 ? candidates[index] : null;
    }

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> roots)
    {
        foreach (var hardware in roots)
        {
            yield return hardware;
            foreach (var child in Flatten(hardware.SubHardware)) yield return child;
        }
    }

    private static ComponentReading BuildComponent(string kind, IHardware? hardware, ComponentSensorKind category)
    {
        var result = new ComponentReading { Kind = kind, Name = hardware?.Name ?? "" };
        if (hardware is null) return result;

        var sensors = Flatten([hardware]).SelectMany(item => item.Sensors).Where(sensor => sensor.Value.HasValue).ToList();
        var samples = sensors.Select(sensor => new SensorSample(sensor.Name, sensor.SensorType, sensor.Value!.Value)).ToList();
        result.Temperature = SensorSelector.Temperature(samples, category, hardware.Name)?.Value;
        result.Load = SensorSelector.Load(samples, category)?.Value;
        result.FanRpm = SensorSelector.Fan(samples)?.Value;
        var clock = SensorSelector.Clock(samples, category, hardware.Name)?.Value;
        var power = SensorSelector.Power(samples, category, hardware.HardwareType)?.Value;
        result.ClockMhz = clock is > 0 ? clock : null;
        result.PowerWatts = power is > .05 ? power : null;

        foreach (var sensor in sensors.Where(IsUseful).OrderBy(s => s.SensorType).ThenBy(s => s.Name))
        {
            var (unit, value) = Format(sensor.SensorType, sensor.Value!.Value);
            result.Details.Add(new SensorReading(sensor.Name, sensor.SensorType.ToString(), value, unit));
        }
        return result;
    }

    private static ComponentReading BuildGpu(IHardware hardware)
    {
        var integrated = IsIntegratedGpuName(hardware.Name) ||
            hardware.HardwareType == HardwareType.GpuIntel && !IsDiscreteGpuName(hardware.Name);
        var reading = BuildComponent(integrated ? LocalizationService.Get("Common.IntegratedGraphics") : LocalizationService.Get("Common.GraphicsCard"), hardware, ComponentSensorKind.Gpu);
        reading.IsIntegratedGpu = integrated;
        return reading;
    }

    public static string? PawnIoUninstallCommand() => ReadPawnIoValue("UninstallString");

    public static bool IsPawnIoInstalled() => ReadPawnIoValue("DisplayVersion") is not null;

    private static string? ReadPawnIoValue(string valueName)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(PawnIoUninstallKey);
                if (key?.GetValue(valueName) is string { Length: > 0 } value) return value;
            }
            catch { }
        }
        return null;
    }

    public void Restart()
    {
        var oldComputer = _computer;
        var oldMemoryTask = _memoryOpenTask;
        try
        {
            if (_opened && oldMemoryTask is { IsCompleted: false })
                _ = oldMemoryTask.ContinueWith(_ => { try { oldComputer.Close(); } catch { } }, TaskScheduler.Default);
            else if (_opened) oldComputer.Close();
        }
        catch { }
        _opened = false;
        _stage = 0;
        _memoryOpenTask = null;
        _computer = CreateComputer();
    }

    private static bool IsSlowGroup(HardwareType type) => type is
        HardwareType.Storage or HardwareType.Motherboard or HardwareType.SuperIO or
        HardwareType.Psu or HardwareType.Cooler or HardwareType.Network;

    private static void UpdateHardwareTree(IHardware hardware, List<IHardware> result, bool update)
    {
        if (result.Contains(hardware)) return;
        result.Add(hardware);
        if (update)
        {
            try { hardware.Update(); }
            catch { }
        }
        try
        {
            foreach (var child in hardware.SubHardware) UpdateHardwareTree(child, result, update);
        }
        catch { }
    }

    private static CpuTemperatureCandidate? FindCpuTemperature(IEnumerable<IHardware> hardware)
    {
        return hardware
            .Where(item => item.HardwareType is not (HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel or HardwareType.Storage))
            .SelectMany(item => item.Sensors.Select(sensor => new { Hardware = item, Sensor = sensor }))
            .Where(item => item.Sensor.SensorType == SensorType.Temperature && item.Sensor.Value is > 0 and < 125)
            .Select(item => new CpuTemperatureCandidate(item.Sensor.Name, item.Sensor.Value!.Value, CpuTemperatureScore(item.Sensor.Name, item.Hardware.HardwareType)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Value)
            .FirstOrDefault();
    }

    private static int CpuTemperatureScore(string name, HardwareType hardwareType)
    {
        if (name.Contains("VRM", StringComparison.OrdinalIgnoreCase) || name.Contains("MOS", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PWM", StringComparison.OrdinalIgnoreCase) || name.Contains("Socket", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase)) return 120;
        if (name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase)) return 115;
        if (name.Contains("CPU (PECI)", StringComparison.OrdinalIgnoreCase)) return 110;
        if (name.Contains("CPU Core", StringComparison.OrdinalIgnoreCase)) return 105;
        if (name.Equals("CPU", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("Processor", StringComparison.OrdinalIgnoreCase)) return 95;
        if (hardwareType == HardwareType.Cpu && (name.Contains("Core", StringComparison.OrdinalIgnoreCase) || name.Contains("Package", StringComparison.OrdinalIgnoreCase))) return 90;
        return 0;
    }

    private double? ReadPublishedMonitorCpuTemperature()
    {
        if (_publishedMonitorMissing) return null;
        foreach (var scope in new[] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" })
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, "SELECT Name, Value FROM Sensor WHERE SensorType = 'Temperature'");
                var candidate = searcher.Get().Cast<ManagementObject>()
                    .Select(item => new { Name = item["Name"]?.ToString() ?? "", Value = TryDouble(item["Value"]) })
                    .Where(item => item.Value is > 0 and < 125)
                    .Select(item => new CpuTemperatureCandidate(item.Name, item.Value!.Value, CpuTemperatureScore(item.Name, HardwareType.Cpu)))
                    .Where(item => item.Score > 0).OrderByDescending(item => item.Score).FirstOrDefault();
                if (candidate is not null) return candidate.Value;
            }
            catch { }
        }
        _publishedMonitorMissing = true;
        return null;
    }

    private static CpuPowerCandidate? FindCpuPower(IEnumerable<IHardware> hardware)
    {
        return hardware
            .Where(item => item.HardwareType is not (HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel or HardwareType.Storage))
            .SelectMany(item => item.Sensors.Select(sensor => new { Hardware = item, Sensor = sensor }))
            .Where(item => item.Sensor.SensorType == SensorType.Power && item.Sensor.Value is >= 0 and < 1000)
            .Select(item => new CpuPowerCandidate(item.Sensor.Name, item.Sensor.Value!.Value, CpuPowerScore(item.Sensor.Name, item.Hardware.HardwareType)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
    }

    private static int CpuPowerScore(string name, HardwareType hardwareType)
    {
        if (name.Contains("GPU", StringComparison.OrdinalIgnoreCase) || name.Contains("DRAM", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase)) return 120;
        if (name.Contains("Package Power", StringComparison.OrdinalIgnoreCase)) return 115;
        if (name.Equals("CPU PPT", StringComparison.OrdinalIgnoreCase) || name.Equals("PPT", StringComparison.OrdinalIgnoreCase)) return 110;
        if (name.Contains("CPU Power", StringComparison.OrdinalIgnoreCase)) return 105;
        if (hardwareType == HardwareType.Cpu && name.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 100;
        return 0;
    }

    private double? ReadPublishedMonitorCpuPower()
    {
        if (_publishedMonitorMissing) return null;
        foreach (var scope in new[] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" })
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, "SELECT Name, Value FROM Sensor WHERE SensorType = 'Power'");
                var candidate = searcher.Get().Cast<ManagementObject>()
                    .Select(item => new { Name = item["Name"]?.ToString() ?? "", Value = TryDouble(item["Value"]) })
                    .Where(item => item.Value is >= 0 and < 1000)
                    .Select(item => new CpuPowerCandidate(item.Name, item.Value!.Value, CpuPowerScore(item.Name, HardwareType.Cpu)))
                    .Where(item => item.Score > 0).OrderByDescending(item => item.Score).FirstOrDefault();
                if (candidate is not null) return candidate.Value;
            }
            catch { }
        }
        _publishedMonitorMissing = true;
        return null;
    }

    private sealed record CpuTemperatureCandidate(string Name, double Value, int Score);
    private sealed record CpuPowerCandidate(string Name, double Value, int Score);

    private static double GpuActivityScore(ComponentReading gpu)
    {
        var score = (gpu.Load ?? 0) * 100 + (gpu.PowerWatts ?? 0) * 2;
        if (gpu.Temperature.HasValue) score += 2;
        if (gpu.Load.HasValue) score += 1;
        if (gpu.IsIntegratedGpu) score += .1;
        return score;
    }

    private static List<VideoInventory> ReadVideoInventory()
    {
        var result = new List<VideoInventory>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, AdapterRAM FROM Win32_VideoController");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || name.Contains("Remote", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new VideoInventory(name, double.TryParse(item["AdapterRAM"]?.ToString(), out var bytes) && bytes > 0 ? bytes / 1073741824d : null));
            }
        }
        catch { }
        return result;
    }

    private void MergeWmiGpuInventory(List<ComponentReading> gpus)
    {
        foreach (var card in _videoInventory)
        {
            if (gpus.Any(g => SimilarGpuName(g.Name, card.Name))) continue;
            var integrated = IsIntegratedGpuName(card.Name);
            var gpu = new ComponentReading { Kind = integrated ? LocalizationService.Get("Common.IntegratedGraphics") : LocalizationService.Get("Common.GraphicsCard"), Name = card.Name, IsIntegratedGpu = integrated };
            if (card.MemoryGb is > 0) gpu.Details.Add(new SensorReading(LocalizationService.Get("Hardware.DeclaredVideoMemory"), "Data", card.MemoryGb.Value, "GB"));
            gpu.Details.Add(new SensorReading(LocalizationService.Get("Hardware.State"), "Status", 0, LocalizationService.Get("Hardware.AsleepOrNoSensor")));
            gpus.Add(gpu);
        }
    }

    private sealed record VideoInventory(string Name, double? MemoryGb);

    private static bool SimilarGpuName(string left, string right)
    {
        static string Clean(string value) => new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()).Replace("tm", "");
        var a = Clean(left); var b = Clean(right);
        return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static readonly string[] DiscreteGpuTokens =
        ["Arc ", "RTX", "GTX", " RX ", "Quadro", "Titan", "Radeon Pro", "Radeon VII", "FirePro", "Instinct"];

    private static readonly string[] IntegratedGpuTokens =
        ["UHD Graphics", "HD Graphics", "Iris", "Vega", "Radeon Graphics", "Radeon(TM) Graphics",
         "610M", "660M", "680M", "740M", "760M", "780M", "860M", "880M", "890M"];

    private static bool IsDiscreteGpuName(string name) => DiscreteGpuTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool IsIntegratedGpuName(string name) =>
        !IsDiscreteGpuName(name) && IntegratedGpuTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string? ReadWmiCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_Processor");
            return searcher.Get().Cast<ManagementObject>()
                .Select(p => p["Name"]?.ToString()?.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        catch { return null; }
    }

    private static void MergeWindowsStorageInventory(List<ComponentReading> storage, List<DiskInventory> inventory)
    {
        foreach (var disk in inventory)
        {
            if (storage.Count >= 4) break;
            if (storage.Any(existing => SimilarStorageName(existing.Name, disk.Name))) continue;
            var reading = new ComponentReading { Kind = $"{MediaLabel(disk.MediaType)} {storage.Count + 1}", Name = disk.Name };
            if (disk.SizeGb is > 0) reading.Details.Add(new SensorReading(LocalizationService.Get("Storage.Capacity"), "Data", disk.SizeGb.Value, "GB"));
            storage.Add(reading);
        }
    }

    // MSFT_PhysicalDisk media types: 3 HDD, 4 SSD, 5 storage-class memory.
    private static string MediaLabel(uint mediaType) => LocalizationService.Get(mediaType switch
    {
        3 => "Storage.Hdd",
        4 => "Storage.Ssd",
        5 => "Storage.Scm",
        _ => "Storage.Generic"
    });

    private static uint MediaTypeFor(string name, List<DiskInventory> inventory) =>
        inventory.FirstOrDefault(disk => SimilarStorageName(disk.Name, name))?.MediaType ?? 0;

    private static List<DiskInventory> ReadPhysicalDiskInventory()
    {
        var result = new List<DiskInventory>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Storage", "SELECT FriendlyName, MediaType, Size FROM MSFT_PhysicalDisk");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["FriendlyName"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new DiskInventory(name, TryUInt(item["MediaType"]), BytesToGb(item["Size"])));
            }
        }
        catch { }
        return result;
    }

    private static List<DiskInventory> ReadDiskDriveInventory()
    {
        var result = new List<DiskInventory>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Model, Size FROM Win32_DiskDrive");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Model"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) result.Add(new DiskInventory(name, 0, BytesToGb(item["Size"])));
            }
        }
        catch { }
        return result;
    }

    private static bool SimilarStorageName(string left, string right)
    {
        static string Clean(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var a = Clean(left); var b = Clean(right);
        return a.Length > 5 && b.Length > 5 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    private static double? TryDouble(object? value) => double.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    private static uint TryUInt(object? value) => uint.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
    private static double? BytesToGb(object? value) => double.TryParse(value?.ToString(), out var bytes) && bytes > 0 ? bytes / 1073741824d : null;
    private sealed record DiskInventory(string Name, uint MediaType, double? SizeGb);

    private static bool IsUseful(ISensor sensor) => sensor.SensorType is SensorType.Temperature or SensorType.Load
        or SensorType.Fan or SensorType.Clock or SensorType.Power or SensorType.Data;

    private static (string Unit, double Value) Format(SensorType type, double value) => type switch
    {
        SensorType.Temperature => ("°C", value),
        SensorType.Load => ("%", value),
        SensorType.Fan => ("RPM", value),
        SensorType.Clock => ("MHz", value),
        SensorType.Power => ("W", value),
        SensorType.Data => ("GB", value),
        _ => ("", value)
    };

    private static List<ComponentReading> BuildFans(List<IHardware> all)
    {
        var fans = new List<ComponentReading>();
        foreach (var hardware in all)
        {
            foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue))
            {
                fans.Add(new ComponentReading
                {
                    Kind = sensor.Name,
                    Name = hardware.Name,
                    FanRpm = sensor.Value
                });
            }
        }
        return fans.Take(8).ToList();
    }

    private static List<ComponentReading> ReadWmiFans()
    {
        var fans = new List<ComponentReading>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, DesiredSpeed FROM Win32_Fan");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                if (!double.TryParse(item["DesiredSpeed"]?.ToString(), out var rpm) || rpm <= 0) continue;
                fans.Add(new ComponentReading { Kind = item["Name"]?.ToString() ?? LocalizationService.Get("Hardware.SystemFan"), Name = LocalizationService.Get("Hardware.AcpiController"), FanRpm = rpm });
            }
        }
        catch { }
        return fans;
    }

    public void Dispose()
    {
        try
        {
            if (_opened && _memoryOpenTask is { IsCompleted: false } opening)
            {
                // The MemoryGroup constructor cannot be cancelled; closing during it would race.
                _ = opening.ContinueWith(_ =>
                {
                    try { _computer.Close(); } catch { }
                }, TaskScheduler.Default);
            }
            else if (_opened) _computer.Close();
        }
        catch { }
        _opened = false;
    }
}
