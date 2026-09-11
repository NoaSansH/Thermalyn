using LibreHardwareMonitor.Hardware;
using System.IO;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Thermalyn.Models;
using Thermalyn.Services;

if (args.Contains("--layout")) { LayoutChecks.Run(); return; }
if (args.Contains("--live-update")) { await UpdateServiceChecks.RunLiveAsync(); return; }
MonitoringChecks.Run();
SessionStatisticsChecks.Run();

if (!UpdateService.TryParseVersion("v1.2.3", out var updateVersion) || updateVersion != new Version(1, 2, 3))
    throw new InvalidOperationException("The updater did not parse a valid GitHub release tag.");
if (UpdateService.TryParseVersion("nightly", out _))
    throw new InvalidOperationException("The updater accepted a non-version release tag.");
var sampleHash = new string('a', 64);
if (UpdateService.ParseExpectedHash($"{sampleHash}  Thermalyn-Setup.exe\n", UpdateService.InstallerAssetName) != sampleHash)
    throw new InvalidOperationException("The updater did not parse SHA256SUMS.txt.");
if (!UpdateService.IsTrustedReleaseAssetUri(
        new Uri("https://github.com/NoaSansH/Thermalyn/releases/download/v1.2.3/Thermalyn-Setup.exe"),
        UpdateService.InstallerAssetName) ||
    UpdateService.IsTrustedReleaseAssetUri(
        new Uri("https://example.com/NoaSansH/Thermalyn/releases/download/v1.2.3/Thermalyn-Setup.exe"),
        UpdateService.InstallerAssetName))
    throw new InvalidOperationException("The updater did not enforce the trusted GitHub asset origin.");
Console.WriteLine("Update version, checksum and origin checks passed.");
await UpdateServiceChecks.RunAsync();

static SensorSample S(string name, SensorType type, double value) => new(name, type, value);
static void Equal(string test, double expected, SensorSample? actual)
{
    if (actual is null || Math.Abs(actual.Value.Value - expected) > 0.001)
        throw new InvalidOperationException($"{test}: expected {expected}, got {actual?.Value.ToString() ?? "null"}");
}
static void Missing(string test, SensorSample? actual)
{
    if (actual is not null) throw new InvalidOperationException($"{test}: expected no measurement, got {actual.Value}");
}
static void Index(string test, int expected, int actual)
{
    if (actual != expected) throw new InvalidOperationException($"{test}: expected index {expected}, got {actual}");
}

var amdCpu = new[]
{
    S("CPU Total", SensorType.Load, 13.4), S("CPU Core Max", SensorType.Load, 67.1),
    S("Core (Tctl/Tdie)", SensorType.Temperature, 90), S("Core (Tdie)", SensorType.Temperature, 85),
    S("CCD1 (Tdie)", SensorType.Temperature, 90),
    S("Cores (Average)", SensorType.Clock, 3250), S("Cores (Average Effective)", SensorType.Clock, 435),
    S("Core #1", SensorType.Clock, 5100), S("Package", SensorType.Power, 50), S("CPU PPT", SensorType.Power, 55)
};
Equal("AMD CPU total load", 13.4, SensorSelector.Load(amdCpu, ComponentSensorKind.Cpu));
Equal("AMD control temperature", 90, SensorSelector.Temperature(amdCpu, ComponentSensorKind.Cpu, "AMD Ryzen Processor"));
Equal("AMD effective frequency", 435, SensorSelector.Clock(amdCpu, ComponentSensorKind.Cpu, "AMD Ryzen Processor"));
Equal("AMD package energy power", 50, SensorSelector.Power(amdCpu, ComponentSensorKind.Cpu, HardwareType.Cpu));

var intelCpu = new[]
{
    S("CPU Total", SensorType.Load, 8), S("CPU Package", SensorType.Temperature, 62),
    S("Core Max", SensorType.Temperature, 68), S("P-Core #1", SensorType.Clock, 4200),
    S("E-Core #1", SensorType.Clock, 3000), S("CPU Package", SensorType.Power, 42),
    S("CPU Cores", SensorType.Power, 31)
};
Equal("Intel package temperature", 62, SensorSelector.Temperature(intelCpu, ComponentSensorKind.Cpu, "Intel Core Ultra"));
Equal("Intel average core clock", 3600, SensorSelector.Clock(intelCpu, ComponentSensorKind.Cpu, "Intel Core Ultra"));
Equal("Intel package power", 42, SensorSelector.Power(intelCpu, ComponentSensorKind.Cpu, HardwareType.Cpu));

var nvidiaGpu = new[]
{
    S("GPU Core", SensorType.Load, 46), S("GPU Memory", SensorType.Load, 34.2),
    S("GPU Power", SensorType.Load, 91), S("GPU Core", SensorType.Temperature, 50),
    S("GPU Hot Spot", SensorType.Temperature, 58), S("GPU Package", SensorType.Power, 72),
    S("12VHPWR Connector", SensorType.Power, 69)
};
Equal("NVIDIA core load, not power percentage", 46, SensorSelector.Load(nvidiaGpu, ComponentSensorKind.Gpu));
Equal("NVIDIA core temperature", 50, SensorSelector.Temperature(nvidiaGpu, ComponentSensorKind.Gpu, "NVIDIA GeForce"));
Equal("NVIDIA package power", 72, SensorSelector.Power(nvidiaGpu, ComponentSensorKind.Gpu, HardwareType.GpuNvidia));

var intelGpu = new[]
{
    S("D3D 3D", SensorType.Load, 19), S("D3D Video Decode", SensorType.Load, 55),
    S("GPU Package", SensorType.Power, 35), S("GPU Total", SensorType.Power, 48)
};
Equal("Intel busiest D3D engine", 55, SensorSelector.Load(intelGpu, ComponentSensorKind.Gpu));
Equal("Intel total board power", 48, SensorSelector.Power(intelGpu, ComponentSensorKind.Gpu, HardwareType.GpuIntel));

var storage = new[]
{
    S("Used Space", SensorType.Load, 81), S("Total Activity", SensorType.Load, 4),
    S("Temperature", SensorType.Temperature, 44), S("Warning Temperature", SensorType.Temperature, 80)
};
Equal("Storage activity, not occupancy", 4, SensorSelector.Load(storage, ComponentSensorKind.Storage));
Equal("Storage current temperature, not warning threshold", 44, SensorSelector.Temperature(storage, ComponentSensorKind.Storage, "NVMe"));

var ambiguous = new[] { S("VRM", SensorType.Temperature, 95), S("CPU Cores", SensorType.Power, 25) };
Missing("No unrelated CPU temperature fallback", SensorSelector.Temperature(ambiguous, ComponentSensorKind.Cpu, "Unknown CPU"));
Missing("No core-only CPU power as package", SensorSelector.Power(ambiguous, ComponentSensorKind.Cpu, HardwareType.Cpu));

var amdApuGpu = new[]
{
    S("GPU Core", SensorType.Load, 3), S("GPU Core", SensorType.Temperature, 45),
    S("GPU Core", SensorType.Power, 10), S("GPU SoC", SensorType.Power, 1)
};
Equal("APU graphics core load", 3, SensorSelector.Load(amdApuGpu, ComponentSensorKind.Gpu));
Equal("APU graphics temperature", 45, SensorSelector.Temperature(amdApuGpu, ComponentSensorKind.Gpu, "AMD Radeon 780M Graphics"));
Missing("No shared APU rail as graphics power", SensorSelector.Power(amdApuGpu, ComponentSensorKind.Gpu, HardwareType.GpuAmd));

var amdDiscreteGpu = new[]
{
    S("GPU Core", SensorType.Power, 180), S("GPU PPT", SensorType.Power, 210),
    S("GPU Package", SensorType.Power, 220), S("GPU SoC", SensorType.Power, 20)
};
Equal("Radeon board power, not chip power", 220, SensorSelector.Power(amdDiscreteGpu, ComponentSensorKind.Gpu, HardwareType.GpuAmd));

var nvidiaWithoutNvml = new[]
{
    S("GPU Core", SensorType.Load, 97), S("GPU Power", SensorType.Load, 88),
    S("GPU Core", SensorType.Temperature, 71)
};
Missing("No graphics power without NVML", SensorSelector.Power(nvidiaWithoutNvml, ComponentSensorKind.Gpu, HardwareType.GpuNvidia));
Equal("NVIDIA load without NVML", 97, SensorSelector.Load(nvidiaWithoutNvml, ComponentSensorKind.Gpu));

Index("Physical memory, not the page file", 1, SensorSelector.PhysicalMemory(
    [new("Virtual Memory", true), new("Total Memory", true), new("Samsung - M425R2GA3PB0 (#0)", false)]));
Index("Single root on older library versions", 0, SensorSelector.PhysicalMemory([new("Generic Memory", true)]));
Index("An unrecognised name still beats the page file", 1, SensorSelector.PhysicalMemory(
    [new("Virtual Memory", true), new("System RAM", true)]));
Index("Page file and DIMMs alone report nothing", -1, SensorSelector.PhysicalMemory(
    [new("Virtual Memory", true), new("DIMM #0", false)]));

var memoryModule = new[]
{
    S("Temperature Sensor Resolution", SensorType.Temperature, 0.25),
    S("Thermal Sensor High Limit", SensorType.Temperature, 55),
    S("DIMM #0", SensorType.Temperature, 55.25),
    S("Thermal Sensor Critical High Limit", SensorType.Temperature, 85)
};
Equal("Module temperature, not its warning limit", 55.25, SensorSelector.ModuleTemperature(memoryModule));
Missing("A limit alone is not a reading", SensorSelector.ModuleTemperature(
    [S("Thermal Sensor High Limit", SensorType.Temperature, 55), S("Capacity", SensorType.Data, 32)]));
Missing("A DIMM suffix is not a current reading", SensorSelector.ModuleTemperature(
    [S("DIMM #0 Max", SensorType.Temperature, 80)]));

var dischargingBattery = new[]
{
    S("Designed Capacity", SensorType.Energy, 52500), S("Fully-Charged Capacity", SensorType.Energy, 46070),
    S("Remaining Capacity", SensorType.Energy, 6550), S("Degradation Level", SensorType.Level, 12.247),
    S("Charge Level", SensorType.Level, 14.217), S("Voltage", SensorType.Voltage, 14.439),
    S("Discharge Current", SensorType.Current, 0.65), S("Discharge Rate", SensorType.Power, 9.399),
    S("Remaining Time (Estimated)", SensorType.TimeSpan, 2508)
};
if (SensorSelector.BatteryIsCharging(dischargingBattery))
    throw new InvalidOperationException("A discharging battery was read as charging.");
Equal("Discharge rate", 9.399, SensorSelector.BatteryRate(dischargingBattery, false));
Equal("Charge level", 14.217, SensorSelector.BatteryLevel(dischargingBattery, "Charge Level"));
Equal("Degradation level", 12.247, SensorSelector.BatteryLevel(dischargingBattery, "Degradation Level"));
Equal("Remaining time while discharging", 2508, SensorSelector.BatteryRemainingTime(dischargingBattery));

var chargingBattery = new[]
{
    S("Designed Capacity", SensorType.Energy, 52500), S("Fully-Charged Capacity", SensorType.Energy, 46060),
    S("Remaining Capacity", SensorType.Energy, 33090), S("Degradation Level", SensorType.Level, 12.266),
    S("Charge Level", SensorType.Level, 71.841), S("Voltage", SensorType.Voltage, 16.754),
    S("Charge Current", SensorType.Current, 1.489), S("Charge Rate", SensorType.Power, 24.963)
};
if (!SensorSelector.BatteryIsCharging(chargingBattery))
    throw new InvalidOperationException("A charging battery was read as discharging.");
Equal("Charge rate", 24.963, SensorSelector.BatteryRate(chargingBattery, true));
// The estimate is only published while draining, so it has to stay absent rather than stale.
Missing("No remaining time while charging", SensorSelector.BatteryRemainingTime(chargingBattery));
Missing("No discharge rate while charging", SensorSelector.BatteryRate(chargingBattery, false));

// A desktop exposes no battery hardware at all, and a battery may omit any of these.
Missing("No charge level without a battery", SensorSelector.BatteryLevel([], "Charge Level"));
Missing("No rate without a battery", SensorSelector.BatteryRate([], false));
Missing("No capacity without a battery", SensorSelector.BatteryCapacity([], "Designed Capacity"));
Missing("No voltage without a battery", SensorSelector.BatteryVoltage([]));
if (SensorSelector.BatteryIsCharging([]))
    throw new InvalidOperationException("Absent battery sensors were read as charging.");
Missing("A percentage over 100 is not a charge level",
    SensorSelector.BatteryLevel([S("Charge Level", SensorType.Level, 140)], "Charge Level"));

Console.WriteLine("Sensor selection checks passed.");

if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
{
    var expected = new AppSettings
    {
        Language = "fr",
        Theme = "Light",
        ViewMode = "Detailed",
        StartWithWindows = true,
        WindowLeft = 42.5,
        WindowTop = 84.5
    };
    var json = JsonSerializer.Serialize(expected);
    var actual = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new InvalidOperationException("Settings JSON deserialized to null.");
    if (actual.SettingsVersion != 10 || actual.Language != expected.Language || actual.Theme != expected.Theme ||
        actual.ViewMode != expected.ViewMode || actual.StartWithWindows != expected.StartWithWindows ||
        actual.WindowLeft != expected.WindowLeft || actual.WindowTop != expected.WindowTop ||
        actual.AccentColor != "#3B9EFF" || actual.HotTemperature != 80 || actual.CriticalTemperature != 95)
        throw new InvalidOperationException("Settings JSON round trip changed the current schema or defaults.");

    Console.WriteLine("Settings: version 10 defaults and JSON schema round trip passed.");
}

if (args.Contains("--repository-contracts", StringComparer.OrdinalIgnoreCase))
{
    var repository = FindRepositoryRoot();
    var themes = Path.Combine(repository, "Thermalyn", "Themes");
    var english = ReadResourceKeys(Path.Combine(themes, "Strings.en.xaml"));
    var french = ReadResourceKeys(Path.Combine(themes, "Strings.fr.xaml"));

    var tables = Directory.GetFiles(themes, "Strings.*.xaml")
        .ToDictionary(path => Path.GetFileNameWithoutExtension(path).Split('.')[^1], StringComparer.Ordinal);

    var offered = LocalizationService.Available.OrderBy(code => code, StringComparer.Ordinal).ToArray();
    var present = tables.Keys.OrderBy(code => code, StringComparer.Ordinal).ToArray();
    if (!offered.SequenceEqual(present, StringComparer.Ordinal))
        throw new InvalidOperationException(
            $"LocalizationService offers [{string.Join(", ", offered)}] but Themes holds [{string.Join(", ", present)}].");

    foreach (var (code, path) in tables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
        if (code.Equals("en", StringComparison.Ordinal)) continue;
        var translated = ReadResourceKeys(path);

        var missing = english.Keys.Except(translated.Keys, StringComparer.Ordinal).OrderBy(key => key).ToArray();
        var unknown = translated.Keys.Except(english.Keys, StringComparer.Ordinal).OrderBy(key => key).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
            throw new InvalidOperationException(
                $"Localization key mismatch in Strings.{code}.xaml. Missing: {string.Join(", ", missing)}; unknown: {string.Join(", ", unknown)}");

        var declaredCode = translated.TryGetValue("Lang.Code", out var value) ? value : "";
        if (!declaredCode.Equals(code, StringComparison.Ordinal))
            throw new InvalidOperationException($"Strings.{code}.xaml declares Lang.Code '{declaredCode}'.");

        foreach (var key in english.Keys)
        {
            if (!PlaceholderSet(english[key]).SetEquals(PlaceholderSet(translated[key])))
                throw new InvalidOperationException($"Localization placeholder mismatch for '{key}' in Strings.{code}.xaml.");
        }
    }

    var mainWindow = File.ReadAllText(Path.Combine(repository, "Thermalyn", "MainWindow.xaml"));
    foreach (var requiredName in new[] { "MiniView", "CompactView", "BalancedView", "DetailedView", "DetailsView", "SettingsView", "ColorPickerOverlay" })
    {
        if (!mainWindow.Contains($"x:Name=\"{requiredName}\"", StringComparison.Ordinal))
            throw new InvalidOperationException($"Required UI surface '{requiredName}' is missing from MainWindow.xaml.");
    }

    if (!mainWindow.Contains("<controls:HistoryChart", StringComparison.Ordinal) ||
        !mainWindow.Contains("<controls:ComponentCard", StringComparison.Ordinal))
        throw new InvalidOperationException("Reusable HistoryChart or ComponentCard is missing from the main UI.");

    var manifest = File.ReadAllText(Path.Combine(repository, "Thermalyn", "app.manifest"));
    if (!manifest.Contains("level=\"asInvoker\"", StringComparison.Ordinal) ||
        manifest.Contains("level=\"requireAdministrator\"", StringComparison.Ordinal))
        throw new InvalidOperationException("The application manifest must use asInvoker so installed launches can relay through the scheduled task without UAC.");

    var installer = File.ReadAllText(Path.Combine(repository, "installer", "Thermalyn.iss"));
    foreach (var contract in new[]
    {
        "AppId={{8F2C1D74-4E63-4A18-9E2B-5C7A0D3F1B96}",
        "ExecAsOriginalUser",
        "procedure CurUninstallStepChanged",
        @"DelTree(Root + '\AppData\Local\{#AppName}'",
        @"DelTree(Root + '\AppData\Local\Temp\.net\{#AppName}'",
        "DelTree(ExpandConstant('{app}')",
        "/delete /f /tn \"\"{#AppName}\"\"",
        "Arguments + ' -silent'",
        "StartupApproved\\Run"
    })
    {
        if (!installer.Contains(contract, StringComparison.Ordinal))
            throw new InvalidOperationException($"Installer contract is missing: {contract}");
    }

    var windowsUsersRoot = @"[a-z]:\\" + @"Users\\";
    var unixUsersRoot = "/" + "home/";
    var privatePathPattern = new Regex(@"(?i)(?:" + windowsUsersRoot + @"[^\\\r\n]+|" + unixUsersRoot + @"[^/\r\n]+)");
    string[] homographs =
    [
        "action", "actions", "application", "applications", "configuration", "format", "image",
        "images", "information", "instance", "message", "messages", "minutes", "normal", "note",
        "notes", "option", "options", "page", "pages", "position", "service", "services", "table",
        "version", "principal", "transparent", "orange", "vertical", "horizontal", "standard",
        "diagnostic", "studio", "charge"
    ];
    var englishWords = Vocabulary(english.Values);
    var frenchOnly = Vocabulary(french.Values)
        .Except(englishWords)
        .Except(homographs)
        .Where(word => word.Length >= 5)
        .Select(Regex.Escape)
        .OrderBy(word => word, StringComparer.Ordinal)
        .ToArray();
    var frenchLiteral = new Regex(
        @"""[^""\r\n]*\b(?:" + string.Join('|', frenchOnly) + @")\b[^""\r\n]*""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    var captionAssignment = new Regex(
        @"\.(?:Text|Content|ToolTip)\s*=\s*(?:""[^""\r\n]*""|[^;\r\n]*?\?\s*""[^""\r\n]*""\s*:\s*""[^""\r\n]*"")",
        RegexOptions.Compiled);
    var readableText = new Regex(@"""[^""\r\n]*\p{L}{3}", RegexOptions.Compiled);
    var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".xaml", ".xml", ".iss", ".ps1", ".md", ".yml", ".yaml", ".json"
    };
    foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(repository, file);
        if (relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !sourceExtensions.Contains(Path.GetExtension(file))) continue;
        var text = File.ReadAllText(file);
        if (privatePathPattern.IsMatch(text))
            throw new InvalidOperationException($"Private absolute user path found in tracked source: {relative}");

        // A stray control character makes a workflow file unparseable, and GitHub is the only
        // thing that says so.
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (!char.IsControl(character) || character is '\r' or '\n' or '\t') continue;
            throw new InvalidOperationException(
                $"{relative} contains the control character U+{(int)character:X4} at offset {index}.");
        }

        if (Path.GetFileName(file).Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{relative} was written by a runtime-specific publish. Regenerate it with: " +
                "dotnet restore tests/Thermalyn.Tests/Thermalyn.Tests.csproj --force-evaluate");

        if (!relative.StartsWith("Thermalyn" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            IsTranslatedStringTable(file) ||
            Path.GetExtension(file) is not (".cs" or ".xaml")) continue;

        var frenchMatch = frenchLiteral.Match(text);
        if (frenchMatch.Success)
            throw new InvalidOperationException(
                $"French wording found in a code literal: {relative} -> \"{frenchMatch.Value}\". Move it to Strings.en.xaml and Strings.fr.xaml.");

        if (Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var caption = captionAssignment.Matches(text).FirstOrDefault(match =>
                readableText.IsMatch(match.Value) && !match.Value.Contains(nameof(LocalizationService), StringComparison.Ordinal));
            if (caption is not null)
                throw new InvalidOperationException(
                    $"Hardcoded caption in {relative} -> \"{caption.Value.Trim()}\". Read it from the string tables instead.");
        }
    }

    // The release workflow reads the section for the version being tagged and fails without it.
    var projectFile = File.ReadAllText(Path.Combine(repository, "Thermalyn", "Thermalyn.csproj"));
    var declared = Regex.Match(projectFile, "<Version>([^<]+)</Version>").Groups[1].Value;
    if (declared.Length == 0) throw new InvalidOperationException("Thermalyn.csproj declares no <Version>.");
    if (!File.ReadAllText(Path.Combine(repository, "CHANGELOG.md")).Contains($"## {declared} ", StringComparison.Ordinal))
        throw new InvalidOperationException($"CHANGELOG.md has no section for version {declared}.");

    // The package manifests describe a published release, so they trail <Version> while the next one
    // is being written. They still have to agree with each other: a half-updated set sends people to
    // one version's URL with another version's checksum.
    var packaging = Path.Combine(repository, "packaging");
    var scoop = File.ReadAllText(Path.Combine(packaging, "scoop", "thermalyn.json"));
    var scoopVersion = Regex.Match(scoop, @"""version""\s*:\s*""([^""]+)""").Groups[1].Value;
    var wingetNames = new[]
    {
        "NoaSansH.Thermalyn.yaml", "NoaSansH.Thermalyn.installer.yaml", "NoaSansH.Thermalyn.locale.en-US.yaml"
    };
    var packaged = "";
    foreach (var name in wingetNames)
    {
        var text = File.ReadAllText(Path.Combine(packaging, "winget", name));
        var version = Regex.Match(text, @"(?m)^PackageVersion:\s*(\S+)\s*$").Groups[1].Value;
        if (version.Length == 0) throw new InvalidOperationException($"{name} declares no PackageVersion.");
        if (packaged.Length == 0) packaged = version;
        else if (!version.Equals(packaged, StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} is at {version} while another WinGet manifest is at {packaged}.");
    }
    if (!scoopVersion.Equals(packaged, StringComparison.Ordinal))
        throw new InvalidOperationException($"The Scoop manifest is at {scoopVersion} and the WinGet manifests at {packaged}.");

    foreach (var (relativeManifest, expected) in new[]
    {
        (Path.Combine("scoop", "thermalyn.json"), $"/releases/download/v{packaged}/Thermalyn-Portable-{packaged}.exe"),
        (Path.Combine("winget", "NoaSansH.Thermalyn.installer.yaml"), $"/releases/download/v{packaged}/Thermalyn-Setup-{packaged}.exe"),
        (Path.Combine("winget", "NoaSansH.Thermalyn.locale.en-US.yaml"), $"/releases/tag/v{packaged}")
    })
    {
        if (!File.ReadAllText(Path.Combine(packaging, relativeManifest)).Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"packaging/{relativeManifest} does not point at {expected}.");
    }

    if (!File.ReadAllText(Path.Combine(repository, "CHANGELOG.md")).Contains($"## {packaged} ", StringComparison.Ordinal))
        throw new InvalidOperationException($"CHANGELOG.md has no section for the packaged version {packaged}.");

    var wingetInstaller = File.ReadAllText(Path.Combine(packaging, "winget", "NoaSansH.Thermalyn.installer.yaml"));

    // Without this dependency an unattended WinGet install leaves a machine with no runtime, which is
    // what the package submission failed on.
    if (!wingetInstaller.Contains("PackageIdentifier: Microsoft.DotNet.DesktopRuntime.8", StringComparison.Ordinal))
        throw new InvalidOperationException("The WinGet installer manifest no longer declares the .NET desktop runtime dependency.");
    if (!Regex.IsMatch(wingetInstaller, @"(?m)^\s*InstallerSha256:\s*[0-9A-F]{64}\s*$"))
        throw new InvalidOperationException("The WinGet installer manifest needs an uppercase 64-character InstallerSha256.");
    if (!Regex.IsMatch(scoop, @"""hash""\s*:\s*""[0-9a-f]{64}"""))
        throw new InvalidOperationException("The Scoop manifest needs a lowercase 64-character hash.");

    Console.WriteLine($"Repository contracts: {english.Count} strings × {tables.Count} languages ({string.Join(", ", present)}), " +
        $"UI, installer, privacy, caption, language and {packaged} packaging checks passed.");
}

if (args.Contains("--startup-registration", StringComparer.OrdinalIgnoreCase))
{
    using (var existingRunKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
    using (var existingApprovalKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
    {
        var existingTaskPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks", "Thermalyn");
        if (existingRunKey?.GetValue("Thermalyn") is not null || existingApprovalKey?.GetValue("Thermalyn") is not null || File.Exists(existingTaskPath))
            throw new InvalidOperationException("Startup integration test refused to overwrite an existing Thermalyn registration.");
    }

    try
    {
        StartupService.SetEnabled(false);
        using (var approvalKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
        {
            if (approvalKey?.GetValue("Thermalyn") is not byte[] { Length: > 0 } disabled || disabled[0] != 3)
                throw new InvalidOperationException("Thermalyn was not registered as disabled in Windows Startup Apps.");
        }

        StartupService.SetEnabled(true);
        using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            var expected = Path.GetFileName(Environment.ProcessPath) ?? "";
            if (runKey?.GetValue("Thermalyn") is not string command || expected.Length == 0 ||
                !command.Contains(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Thermalyn is missing from the standard Windows Run entries.");
        }

        var taskPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks", "Thermalyn");
        if (!File.Exists(taskPath)) throw new InvalidOperationException("The elevated startup task was not created.");

        if (!StartupService.TryLaunchRegisteredTask())
            throw new InvalidOperationException("The verified elevated startup task could not be launched.");
        Console.WriteLine("Startup registration: Run entry, approval state and elevated launch passed.");
    }
    finally
    {
        StartupService.RemoveRegistration();
    }
}

if (args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
{
    var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = false, IsBatteryEnabled = true };
    try
    {
        computer.Open();
        foreach (var hardware in computer.Hardware)
        {
            hardware.Update();
            foreach (var child in hardware.SubHardware) child.Update();
            if (hardware.HardwareType == HardwareType.Battery)
            {
                Console.WriteLine($"{hardware.HardwareType}: {hardware.Name}");
                foreach (var sensor in hardware.Sensors)
                    Console.WriteLine($"  {sensor.SensorType,-12} {sensor.Name,-28} = {sensor.Value?.ToString() ?? "null"}");
                continue;
            }
            if (hardware.HardwareType == HardwareType.Memory)
            {
                Console.WriteLine($"{hardware.HardwareType}: {hardware.Name}");
                foreach (var sensor in hardware.Sensors)
                    Console.WriteLine($"  {sensor.SensorType,-12} {sensor.Name,-28} = {sensor.Value?.ToString() ?? "null"}");
                foreach (var sub in hardware.SubHardware)
                {
                    Console.WriteLine($"  sub: {sub.Name}");
                    foreach (var sensor in sub.Sensors)
                        Console.WriteLine($"    {sensor.SensorType,-12} {sensor.Name,-28} = {sensor.Value?.ToString() ?? "null"}");
                }
                continue;
            }
            if (hardware.HardwareType is not (HardwareType.Cpu or HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia)) continue;

            var samples = hardware.Sensors.Where(sensor => sensor.Value.HasValue)
                .Select(sensor => S(sensor.Name, sensor.SensorType, sensor.Value!.Value)).ToList();
            var kind = hardware.HardwareType == HardwareType.Cpu ? ComponentSensorKind.Cpu : ComponentSensorKind.Gpu;
            Console.WriteLine($"{hardware.HardwareType}: {hardware.Name}");
            Console.WriteLine($"  temperature={SensorSelector.Temperature(samples, kind, hardware.Name)}");
            Console.WriteLine($"  load={SensorSelector.Load(samples, kind)}");
            Console.WriteLine($"  clock={SensorSelector.Clock(samples, kind, hardware.Name)}");
            Console.WriteLine($"  power={SensorSelector.Power(samples, kind, hardware.HardwareType)}");
            foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Power))
                Console.WriteLine($"  raw power: {sensor.Name}={sensor.Value?.ToString() ?? "null"} W");
        }
    }
    finally { computer.Close(); }

    using var service = new HardwareMonitorService();
    for (var index = 1; index <= 6; index++)
    {
        var watch = Stopwatch.StartNew();
        var snapshot = service.Read();
        Console.WriteLine($"service read {index}: {watch.ElapsedMilliseconds} ms; " +
            $"CPU {snapshot.Cpu.Load:0.0}% {snapshot.Cpu.Temperature:0.0} C {snapshot.Cpu.ClockMhz:0} MHz {snapshot.Cpu.PowerWatts:0.0} W; " +
            $"GPU {snapshot.Gpu.Load:0.0}% {snapshot.Gpu.Temperature:0.0} C {snapshot.Gpu.PowerWatts:0.0} W; " +
            $"RAM {snapshot.Memory.Load:0.0}%");
        foreach (var battery in snapshot.Batteries)
            Console.WriteLine($"  battery {battery.Name}: {battery.ChargePercent:0.0}% " +
                $"{(battery.IsCharging ? "charging" : "draining")} {battery.RateWatts:0.0} W, " +
                $"health {battery.HealthPercent:0.0}%, {battery.RemainingWattHours:0.0}/{battery.FullChargeWattHours:0.0} " +
                $"of {battery.DesignedWattHours:0.0} Wh, left {battery.RemainingTime?.ToString() ?? "n/a"}");
        Thread.Sleep(300);
    }
}

static HashSet<string> Vocabulary(IEnumerable<string> values) =>
    values.SelectMany(value => Regex.Matches(value, @"[\p{L}']+").Select(match => match.Value.ToLowerInvariant()))
        .ToHashSet(StringComparer.Ordinal);

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Thermalyn", "Thermalyn.csproj")))
            return directory.FullName;
    }

    throw new DirectoryNotFoundException("Could not find the Thermalyn repository root.");
}

static Dictionary<string, string> ReadResourceKeys(string path)
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var entries = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var element in XDocument.Load(path).Root?.Elements() ?? [])
    {
        var key = (string?)element.Attribute(x + "Key");
        if (string.IsNullOrWhiteSpace(key)) continue;
        if (!entries.TryAdd(key, element.Value))
            throw new InvalidOperationException($"Duplicate localization key '{key}' in {path}.");
    }

    return entries;
}

static bool IsTranslatedStringTable(string path)
{
    var name = Path.GetFileName(path);
    return name.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase) &&
           name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) &&
           !name.Equals("Strings.en.xaml", StringComparison.OrdinalIgnoreCase);
}

static HashSet<string> PlaceholderSet(string value) =>
    Regex.Matches(value, @"\{\d+(?:[^}]*)?\}")
        .Select(match => Regex.Match(match.Value, @"\d+").Value)
        .ToHashSet(StringComparer.Ordinal);
