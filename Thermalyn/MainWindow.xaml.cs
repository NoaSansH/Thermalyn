using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Thermalyn.Models;
using Thermalyn.Services;
using Thermalyn.ViewModels;

namespace Thermalyn;

public partial class MainWindow : Window
{
    private readonly HardwareMonitorService _hardware = new();
    private readonly ProcessMonitorService _processes = new();
    private readonly SettingsService _settingsService = new();
    private readonly ThermalEventService _events = new();
    private readonly MainWindowViewModel _viewModel = new();
    private readonly DispatcherTimer _timer = new();
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();
    private readonly Queue<double> _ramHistory = new();
    private readonly Queue<double> _cpuLoadHistory = new();
    private readonly Queue<double> _gpuLoadHistory = new();
    private AppSettings _settings = new();
    private HardwareSnapshot _snapshot = new();
    private List<ProcessUsage> _processSnapshot = [];
    private Queue<double>? _detailHistory;
    private Func<ComponentReading?>? _detailReader;
    private bool _detailShowsTemperature;
    private bool _isReading;
    private bool _syncingColorWheel;
    private TextBox? _pickerTarget;
    private HwndSource? _windowSource;
    private bool _panelOverlayMode;
    private bool _driverBannerDismissed;
    private bool _driverWasMissing;
    private bool _driverInstallStarted;
    private bool _purgeArmed;
    private bool _purgeRequested;
    private bool _firstRenderDone;
    private int _listTick;

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");

    private readonly List<(UniformGrid Grid, UIElement Cpu, UIElement Gpu)> _cardPairs = [];
    private UIElement[] _secondaryOrder = [];
    private UIElement[] _miniOrder = [];

    private static readonly Dictionary<string, (string Accent, string Normal, string Hot, string Critical)> Palettes = new()
    {
        ["Studio"] = ("#3B9EFF", "#3B9EFF", "#FFA83B", "#FF5C5C"),
        ["Slate"] = ("#8FB4D9", "#7FA8D9", "#E0A85E", "#E87676"),
        ["Prism"] = ("#A97BFF", "#55B8FF", "#FFAE52", "#FF6B85"),
        ["Mint"] = ("#35CFB4", "#35CFB4", "#F0B355", "#FF6E6E"),
        ["Copper"] = ("#FF9A4D", "#7FD16B", "#FFB03B", "#FF6A50"),
        ["Dawn"] = ("#1F6FB5", "#1F6FB5", "#B5730F", "#C43333"),
        ["Sage"] = ("#2E7D5B", "#2E7D5B", "#A8700F", "#C03B3B"),
        ["Sand"] = ("#A0662A", "#2E7A63", "#A86A10", "#BC3F32"),
        ["Ink"] = ("#2C4C73", "#2C4C73", "#9C6C14", "#B33A44"),
        ["Garnet"] = ("#A62F44", "#216E86", "#A06714", "#C22B3A"),
    };

    private static readonly int[] HotThresholds = Enumerable.Range(8, 13).Select(i => i * 5).ToArray();
    private static readonly int[] CriticalThresholds = Enumerable.Range(9, 14).Select(i => i * 5).ToArray();

    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMaxButton = 9;
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;

    private readonly bool _previewMode;

    public MainWindow() : this(false) { }

    internal MainWindow(bool previewMode)
    {
        _previewMode = previewMode;
        Controls.PrecisionScrolling.Enable();
        InitializeComponent();
        foreach (var grid in new[] { HeroGrid, CompactCardsGrid, DetailedChartGrid, DetailedSensorGrid })
            _cardPairs.Add((grid, grid.Children[0], grid.Children[1]));
        _secondaryOrder = SecondaryMetrics.Children.Cast<UIElement>().ToArray();
        _miniOrder = MiniCardsGrid.Children.Cast<UIElement>().ToArray();
        AppVersionText.Text = $"Thermalyn {typeof(App).Assembly.GetName().Version?.ToString(3)}".TrimEnd();
        DataContext = _viewModel;
        if (!previewMode) { Loaded += OnLoaded; Closed += OnClosed; }
        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => { UpdateCaptionGlyph(); MinimizeToNotificationArea(); };
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        StartLoadingAnimation();
        HotThresholdCombo.SelectionChanged += (_, _) => ValidateThresholds();
        CriticalThresholdCombo.SelectionChanged += (_, _) => ValidateThresholds();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DeploymentService.IsInstalled())
            PurgeSectionTitle.Visibility = PurgeSection.Visibility = Visibility.Collapsed;
        _settings = _settingsService.Load();
        try { _settings.StartWithWindows = StartupService.EnsureListed(_settings.StartWithWindows); }
        catch (Exception exception) { Log($"Startup registration repair: {exception.Message}"); }
        LocalizationService.Apply(_settings.Language);
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top && IsVisiblePosition(left, top)) { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        ApplyTheme(_settings.Theme);
        Topmost = _settings.AlwaysOnTop;
        LoadSettingsControls();
        ApplyMode(_settings.ViewMode);
        ApplyResponsiveLayout();
        _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.RefreshSeconds, 1, 5));
        UpdateTray();
        _events.Load();
        BuildThermalEvents();
        StartUpdateMonitoring();

        SetStatus(LocalizationService.Get("Status.QuickRead"));
        _snapshot = await Task.Run(QuickReadService.Read);
        await RenderAsync(false);
        HideLoadingOverlay();
        SetStatus(LocalizationService.Get("Status.OpeningSensors"));
        _timer.Start();
        await RefreshAsync();
    }

    private static bool IsVisiblePosition(double left, double top) => left > SystemParameters.VirtualScreenLeft - 50 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 && top > SystemParameters.VirtualScreenTop - 50 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    private const int ProcessInterval = 3;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(45);
    private int _processTick;
    private Task<HardwareSnapshot>? _pendingRead;

    private async Task RefreshAsync()
    {
        if (_isReading || _isClosed) return;
        _isReading = true;
        try
        {
            var hardwareTask = _pendingRead ?? Task.Run(_hardware.Read);
            _pendingRead = null;
            var readProcesses = _settings.ShowProcesses && (_processSnapshot.Count == 0 || _processTick++ % ProcessInterval == 0);
            var processTask = readProcesses ? Task.Run(_processes.Read) : Task.FromResult(_processSnapshot);
            try
            {
                await Task.WhenAll(hardwareTask, processTask).WaitAsync(ReadTimeout);
            }
            catch (TimeoutException)
            {
                _pendingRead = hardwareTask;
                SetStatus(LocalizationService.Get("Status.SensorsBusy"));
                Log($"Hardware read still pending after {ReadTimeout.TotalSeconds:0} s.");
                if (_firstRenderDone) HideLoadingOverlay();
                return;
            }
            _snapshot = await hardwareTask;
            if (_isClosed) return;
            UpdateTray();
            CheckThermalNotifications();
            if (processTask.IsCompletedSuccessfully) _processSnapshot = processTask.Result;
            await RenderAsync(false);
        }
        catch (Exception ex)
        {
            _snapshot = new HardwareSnapshot { Error = ex.Message };
            SetStatus(LocalizationService.Get("Status.ReadError"));
            Log(ex.ToString());
            HideLoadingOverlay();
        }
        finally { _isReading = false; }
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        MiniStatusText.Text = message;
        CompactStatusText.Text = message;
        DetailedStatusText.Text = message;
        if (LoadingOverlay.Visibility == Visibility.Visible) LoadingMessage.Text = message;
    }

    private static void DeleteSettingsFolder()
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thermalyn");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch { }
    }

    private void StartLoadingAnimation()
    {
        var slide = new DoubleAnimation(-70, 200, TimeSpan.FromSeconds(1.4))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        LoadingSlide.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void HideLoadingOverlay()
    {
        if (LoadingOverlay.Visibility != Visibility.Visible) return;
        LoadingSlide.BeginAnimation(TranslateTransform.XProperty, null);
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private static void Log(string message)
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thermalyn");
            Directory.CreateDirectory(folder);
            File.AppendAllText(System.IO.Path.Combine(folder, "diagnostic.log"), $"{DateTime.Now:u}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    private async Task RenderAsync(bool staged = true)
    {
        var cpuFan = _snapshot.Cpu.FanRpm ?? _snapshot.Fans
            .FirstOrDefault(fan => fan.Kind.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                                   fan.Kind.Contains("Processor", StringComparison.OrdinalIgnoreCase))?.FanRpm;
        AppendHistory(_cpuLoadHistory, _snapshot.Cpu.Load);
        AppendHistory(_gpuLoadHistory, _snapshot.Gpu.Load);
        AppendHistory(_ramHistory, _snapshot.Memory.Load);
        TrackAndRenderSession();

        UpdateCard(_snapshot.Cpu, CpuHeroCard.Secondary, CpuHeroCard.Fan, cpuFan, _cpuHistory);
        CpuHeroCard.NameValue.Text = _snapshot.Cpu.Name;
        DetailedCpuName.Text = CpuHeroCard.NameValue.Text;
        RenderPrimaryMetric(_snapshot.Cpu, CpuHeroCard.Value, CpuHeroCard.Unit, CpuHeroCard.State, CpuHeroCard.SecondaryCaption, CpuHeroCard.Secondary, _settings.CpuPrimaryMetric);
        RenderPrimaryMetric(_snapshot.Cpu, CompactCpuTemp, CompactCpuUnit, CompactCpuState, CompactCpuSecondaryLabel, CompactCpuStats, _settings.CpuPrimaryMetric);
        CpuClock.Text = Mhz(_snapshot.Cpu.ClockMhz); CpuPower.Text = Watts(_snapshot.Cpu.PowerWatts); CpuHeroCard.SummaryTemperature.Text = Temperature(_snapshot.Cpu.Temperature);
        CpuHeroCard.SummaryThirdValue.Text = CpuClock.Text; CpuHeroCard.SummaryFourthValue.Text = CpuPower.Text;
        DetailedCpuValue.Text = HeroTemperature(_snapshot.Cpu.Temperature); DetailedCpuValue.Foreground = TemperatureBrush(_snapshot.Cpu.Temperature); DetailedCpuUnit.Text = _settings.TemperatureUnit == "F" ? "°F" : "°C";
        DetailedCpuState.Text = ThermalState(_snapshot.Cpu.Temperature); DetailedCpuRange.Text = RangeText(_cpuHistory);
        DetailedCpuLoad.Text = Percent(_snapshot.Cpu.Load); DetailedCpuClock.Text = Mhz(_snapshot.Cpu.ClockMhz); DetailedCpuPower.Text = Watts(_snapshot.Cpu.PowerWatts); DetailedCpuFan.Text = Rpm(cpuFan);
        ShowIfMeasured(CpuHeroCard.FanHost, CpuHeroCard.Fan, _settings.ShowFans);
        ShowIfMeasured(DetailedCpuFanMetric, DetailedCpuFan, _settings.ShowFans);
        ShowIfMeasured(CpuClockMetric, CpuClock);
        ShowIfMeasured(CpuPowerMetric, CpuPower);
        RenderCpuChart(); RenderDetailedCpuChart();

        if (staged) await Task.Delay(55);

        UpdateCard(_snapshot.Gpu, GpuHeroCard.Secondary, GpuHeroCard.Fan, _snapshot.Gpu.FanRpm, _gpuHistory);
        var integrated = _snapshot.Gpu.IsIntegratedGpu;
        GpuHeroCard.NameValue.Text = $"{_snapshot.Gpu.Name} · {LocalizationService.Get("Common.Active")}";
        DetailedGpuName.Text = GpuHeroCard.NameValue.Text;
        _viewModel.GpuHeading = CompactGpuTitle.Text = DetailedGpuHeading.Text = integrated ? LocalizationService.Get("Head.IntegratedGraphics") : LocalizationService.Get("Head.GraphicsCard");
        DetailedGpuSensorsTitle.Text = integrated ? LocalizationService.Get("Head.SensorsIntegratedGraphics") : LocalizationService.Get("Head.SensorsGraphicsCard");
        DetailedGpuKind.Text = integrated ? LocalizationService.Get("Common.Integrated") : LocalizationService.Get("Common.Discrete");
        RenderPrimaryMetric(_snapshot.Gpu, GpuHeroCard.Value, GpuHeroCard.Unit, GpuHeroCard.State, GpuHeroCard.SecondaryCaption, GpuHeroCard.Secondary, _settings.GpuPrimaryMetric);
        RenderPrimaryMetric(_snapshot.Gpu, CompactGpuTemp, CompactGpuUnit, CompactGpuState, CompactGpuSecondaryLabel, CompactGpuStats, _settings.GpuPrimaryMetric);
        GpuPower.Text = Watts(_snapshot.Gpu.PowerWatts); GpuHeroCard.SummaryTemperature.Text = Temperature(_snapshot.Gpu.Temperature);
        GpuHeroCard.SummaryThirdLabel.Text = LocalizationService.Get("Common.Power"); GpuHeroCard.SummaryThirdValue.Text = GpuPower.Text;
        GpuHeroCard.SummaryFourthLabel.Text = LocalizationService.Get("Common.Ram"); GpuHeroCard.SummaryFourthValue.Text = BalancedRamMetric.Text;
        DetailedGpuValue.Text = HeroTemperature(_snapshot.Gpu.Temperature); DetailedGpuValue.Foreground = TemperatureBrush(_snapshot.Gpu.Temperature, ThermalComponent.Gpu); DetailedGpuUnit.Text = DetailedCpuUnit.Text;
        DetailedGpuState.Text = ThermalState(_snapshot.Gpu.Temperature, ThermalComponent.Gpu); DetailedGpuRange.Text = RangeText(_gpuHistory);
        DetailedGpuLoad.Text = Percent(_snapshot.Gpu.Load); DetailedGpuPower.Text = Watts(_snapshot.Gpu.PowerWatts); DetailedGpuFan.Text = Rpm(_snapshot.Gpu.FanRpm);
        ShowIfMeasured(GpuHeroCard.FanHost, GpuHeroCard.Fan, _settings.ShowFans);
        ShowIfMeasured(DetailedGpuFanMetric, DetailedGpuFan, _settings.ShowFans);
        ShowIfMeasured(GpuPowerMetric, GpuPower);
        RenderGpuChart(); RenderDetailedGpuChart();

        if (staged) await Task.Delay(55);

        RamValue.Text = CompactRamValue.Text = MemorySummary(_snapshot.Memory);
        RamBar.Value = CompactRamBar.Value = DetailedRamBar.Value = _snapshot.Memory.Load ?? 0;
        RamPercentValue.Text = CompactRam.Text = BalancedRamMetric.Text = Percent(_snapshot.Memory.Load);
        GpuHeroCard.SummaryFourthValue.Text = BalancedRamMetric.Text;
        DetailedRamName.Text = _snapshot.Memory.Name;
        DetailedRamSummary.Text = MemoryHeaderSummary(_snapshot.Memory);
        DetailedRamPercent.Text = _snapshot.Memory.Load is double ramLoad ? $"{ramLoad:0}" : "—";
        RenderDetailedRamChart();
        if (!_firstRenderDone || ++_listTick % 3 == 0)
        {
            BuildStorage(); BuildFans();
            if (staged) await Task.Delay(55);
            BuildGpuDetails(); BuildDetailedSystem(); BuildDetailedSensorPanels();
            NormalizeDividers(BalancedSystemList); NormalizeDividers(CompactSystemList);
            if (staged) await Task.Delay(55);
            BuildProcessRankings();
        }
        if (TrackThermalEvents()) BuildThermalEvents();
        _firstRenderDone = true;
        HideLoadingOverlay();
        UpdateHardwareDriverBanner();
        if (DetailsView.Visibility == Visibility.Visible && _detailReader?.Invoke() is { } detail)
            ShowDetails(detail);

        if (_snapshot.Error is { Length: > 0 }) { SetStatus(LocalizationService.Get("Status.SomeSensorsSilent")); Log($"Snapshot error: {_snapshot.Error}"); }
        else if (!_hardware.IsFullyOpen) SetStatus(LocalizationService.Get("Status.StillOpening"));
        else if (!_snapshot.Cpu.Temperature.HasValue) SetStatus(LocalizationService.Get("Status.NoCpuTemperature"));
        else SetStatus(LocalizationService.Format("Status.UpdatedAt", $"{_snapshot.Timestamp:HH:mm:ss}"));
    }

    private string HeroTemperature(double? value) => value is null ? "—" : $"{DisplayTemperatureValue(value):0}";
    private string ThermalState(double? value, ThermalComponent component = ThermalComponent.Cpu) => value is null ? (_snapshot.IsPartial ? LocalizationService.Get("Common.Measuring") : LocalizationService.Get("Common.SensorUnavailable"))
        : value >= ThermalThresholds.For(_settings, component).Critical ? LocalizationService.Get("Common.Critical")
        : value >= ThermalThresholds.For(_settings, component).Hot ? LocalizationService.Get("Common.Hot") : LocalizationService.Get("Common.Normal");
    private string RangeText(Queue<double> history) => history.Count == 0 ? "—" : $"{Temperature(history.Min())} / {Temperature(history.Max())}";
    private static string MemorySummary(ComponentReading memory)
    {
        var total = memory.Details.FirstOrDefault(d => d.Name == LocalizationService.Get("Hardware.PhysicalTotal"));
        var used = memory.Details.FirstOrDefault(d => d.Name == LocalizationService.Get("Hardware.PhysicalUsed"));
        if (total is null) return "";
        return used is null ? $"· {total.Value:0.#} {total.Unit}" : $"· {used.Value:0.#} / {total.Value:0.#} " + LocalizationService.Get("Unit.Gigabytes");
    }

    private string MemoryHeaderSummary(ComponentReading memory)
    {
        var usage = MemorySummary(memory).TrimStart('·', ' ');
        var temperature = memory.Temperature is null ? "" : Temperature(memory.Temperature);
        return string.Join(" · ", new[] { usage, temperature }.Where(value => value.Length > 0));
    }

    private void UpdateCard(ComponentReading reading, TextBlock load, TextBlock fanText, double? fan, Queue<double> history)
    {
        load.Text = Percent(reading.Load);
        fanText.Text = Rpm(fan);
        AppendHistory(history, reading.Temperature);
    }

    private int HistoryCapacity => Math.Max(180, 900 / Math.Max(1, _settings.RefreshSeconds));
    private void AppendHistory(Queue<double> history, double? value)
    {
        if (value is not double sample) return;
        history.Enqueue(sample);
        while (history.Count > HistoryCapacity) history.Dequeue();
    }

    private void RenderPrimaryMetric(ComponentReading reading, TextBlock value, TextBlock unit, TextBlock state, TextBlock secondaryLabel, TextBlock secondaryValue, string metric)
    {
        if (metric == "Load")
        {
            value.Text = reading.Load is double load ? $"{load:0}" : "—";
            unit.Text = "%";
            value.Foreground = FindBrush("AccentBrush");
            state.Text = reading.Load is double current
                ? LocalizationService.Format("Chart.LoadPercent", current)
                : LocalizationService.Get("Chart.LoadUnavailable");
            secondaryLabel.Text = LocalizationService.Get("Common.Temperature");
            secondaryValue.Text = Temperature(reading.Temperature);
            secondaryValue.Foreground = TemperatureBrush(reading.Temperature, ComponentKind(reading));
        }
        else
        {
            value.Text = HeroTemperature(reading.Temperature);
            unit.Text = _settings.TemperatureUnit == "F" ? "°F" : "°C";
            value.Foreground = TemperatureBrush(reading.Temperature, ComponentKind(reading));
            state.Text = ThermalState(reading.Temperature, ComponentKind(reading));
            secondaryLabel.Text = LocalizationService.Get("Common.Load");
            secondaryValue.Text = Percent(reading.Load);
            secondaryValue.Foreground = FindBrush("TextPrimaryBrush");
        }
    }

    private Queue<double> PrimaryData(Queue<double> temperature, Queue<double> load, string metric) => metric == "Load" ? load : temperature;
    private Brush PrimaryStroke(Queue<double> temperature, string metric, ThermalComponent component) => metric == "Load" || temperature.Count == 0 ? FindBrush("AccentBrush") : TemperatureBrush(temperature.Last(), component);

    private void RenderCpuChart() => UpdateChart(CpuHeroCard.History, PrimaryData(_cpuHistory, _cpuLoadHistory, _settings.CpuPrimaryMetric),
        _settings.CpuPrimaryMetric != "Load", PrimaryStroke(_cpuHistory, _settings.CpuPrimaryMetric, ThermalComponent.Cpu), $"CPU · {LocalizationService.Get(_settings.CpuPrimaryMetric == "Load" ? "Common.Load" : "Common.Temperature")}");
    private void RenderGpuChart() => UpdateChart(GpuHeroCard.History, PrimaryData(_gpuHistory, _gpuLoadHistory, _settings.GpuPrimaryMetric),
        _settings.GpuPrimaryMetric != "Load", PrimaryStroke(_gpuHistory, _settings.GpuPrimaryMetric, ThermalComponent.Gpu), $"GPU · {LocalizationService.Get(_settings.GpuPrimaryMetric == "Load" ? "Common.Load" : "Common.Temperature")}");
    private void RenderDetailedCpuChart() => UpdateChart(DetailedCpuHistory, _cpuHistory, true, TemperatureBrush(_snapshot.Cpu.Temperature), LocalizationService.Get("Detail.CpuTooltip"));
    private void RenderDetailedGpuChart() => UpdateChart(DetailedGpuHistory, _gpuHistory, true, TemperatureBrush(_snapshot.Gpu.Temperature, ThermalComponent.Gpu), LocalizationService.Get("Detail.GpuTooltip"));
    private void RenderDetailedRamChart() => UpdateChart(DetailedRamHistory, _ramHistory, false, FindBrush("AccentBrush"), LocalizationService.Get("Detail.RamTooltip"));

    private void UpdateChart(Controls.HistoryChart chart, IEnumerable<double> data, bool isTemperature, Brush stroke, string label)
    {
        chart.Data = data.ToArray();
        chart.IsTemperature = isTemperature;
        chart.Stroke = stroke;
        chart.Label = label;
        chart.Unit = isTemperature ? (_settings.TemperatureUnit == "F" ? "°F" : "°C") : "%";
        chart.SampleIntervalSeconds = Math.Max(1, _settings.RefreshSeconds);
    }

    private void DrawAllHistory()
    {
        RenderCpuChart(); RenderGpuChart();
        RenderDetailedCpuChart(); RenderDetailedGpuChart(); RenderDetailedRamChart();
    }

    private void ApplyCardOrder()
    {
        foreach (var (grid, cpu, gpu) in _cardPairs)
            Reorder(grid, _settings.GpuFirst ? [gpu, cpu] : [cpu, gpu]);

        Reorder(MiniCardsGrid, _settings.GpuFirst
            ? [_miniOrder[1], _miniOrder[0], _miniOrder[2]]
            : _miniOrder);

        Reorder(SecondaryMetrics, _settings.GpuFirst
            ? _secondaryOrder.OrderBy(child => ReferenceEquals(child, GpuPowerMetric) ? 0 : 1).ToArray()
            : _secondaryOrder);
    }

    private static void Reorder(Panel panel, IReadOnlyList<UIElement> children)
    {
        if (panel.Children.Cast<UIElement>().SequenceEqual(children)) return;
        panel.Children.Clear();
        foreach (var child in children) panel.Children.Add(child);
    }

    private void ApplyMode(string mode)
    {
        mode = mode is "Mini" or "Compact" or "Balanced" or "Detailed" ? mode : "Balanced";
        ApplyCardOrder();
        _settings.ViewMode = mode; _panelOverlayMode = false; DetailsView.Visibility = SettingsView.Visibility = Visibility.Collapsed; HideMainViews();
        (mode switch { "Mini" => MiniView, "Compact" => CompactView, "Detailed" => DetailedView, _ => BalancedView }).Visibility = Visibility.Visible;
        MarkMode(MiniButton, MiniNavIcon, mode == "Mini"); MarkMode(CompactButton, CompactNavIcon, mode == "Compact"); MarkMode(BalancedButton, BalancedNavIcon, mode == "Balanced"); MarkMode(DetailedButton, DetailedNavIcon, mode == "Detailed");
        ApplyDataVisibility();
        ApplyResponsiveLayout();
        DrawAllHistory();
    }

    private void ApplyDataVisibility()
    {
        var showGraphs = _settings.ShowGraphs;
        var graphVisibility = showGraphs ? Visibility.Visible : Visibility.Collapsed;
        CpuHeroCard.History.Visibility = GpuHeroCard.History.Visibility = graphVisibility;
        CpuHeroCard.Summary.Visibility = GpuHeroCard.Summary.Visibility = showGraphs ? Visibility.Collapsed : Visibility.Visible;
        DetailedCpuHistory.Visibility = DetailedGpuHistory.Visibility = DetailedRamHistory.Visibility = graphVisibility;

        var fanVisibility = _settings.ShowFans ? Visibility.Visible : Visibility.Collapsed;
        FanSection.Visibility = fanVisibility;
        if (!_settings.ShowFans)
            CpuHeroCard.FanHost.Visibility = GpuHeroCard.FanHost.Visibility = DetailedCpuFanMetric.Visibility = DetailedGpuFanMetric.Visibility = Visibility.Collapsed;
    }

    private bool TrackThermalEvents()
    {
        var changed = _events.Track(LocalizationService.Get("Common.Processor"), _snapshot.Cpu.Temperature, _settings.HotTemperature, _settings.CriticalTemperature);
        changed |= _events.Track(_snapshot.Gpu.IsIntegratedGpu ? LocalizationService.Get("Common.IntegratedGraphics") : LocalizationService.Get("Head.GraphicsCard"), _snapshot.Gpu.Temperature, _settings.GpuHotTemperature, _settings.GpuCriticalTemperature);
        foreach (var drive in _snapshot.Storage)
            changed |= _events.Track(drive.Kind, drive.Temperature, _settings.StorageHotTemperature, _settings.StorageCriticalTemperature);
        return changed;
    }

    private void BuildThermalEvents()
    {
        ThermalEventsPanel.Children.Clear();
        if (_events.Entries.Count == 0)
        {
            ThermalEventsPanel.Children.Add(EmptyRow(LocalizationService.Get("Empty.NoExceedance")));
            NormalizeDividers(ThermalEventsPanel);
            ClearEventsButton.Visibility = Visibility.Collapsed;
            return;
        }
        ClearEventsButton.Visibility = Visibility.Visible;
        foreach (var entry in _events.Entries.Take(12))
        {
            var minutes = Math.Max(1, (int)Math.Round((entry.End - entry.Start).TotalMinutes));
            var when = entry.Start.ToString(entry.Start.Date == DateTime.Today ? "HH:mm" : "dd MMM HH:mm", CultureInfo.CurrentCulture);
            var brush = ThermalEventService.IsCritical(entry.Level) ? FindBrush("TempCriticalBrush") : FindBrush("TempHotBrush");
            ThermalEventsPanel.Children.Add(ListRow(Glyph("IconThermometer"), $"{entry.Component} · {ThermalEventService.Describe(entry.Level)}",
                $"{when} · {minutes} min", $"max {Temperature(entry.Peak)}", brush));
        }
        NormalizeDividers(ThermalEventsPanel);
    }

    private void ClearEvents_Click(object sender, RoutedEventArgs e)
    {
        _events.Clear();
        BuildThermalEvents();
    }

    private void UpdateHardwareDriverBanner()
    {
        var noTemperature = !_snapshot.Cpu.Temperature.HasValue;
        var noPower = !_snapshot.Cpu.PowerWatts.HasValue;
        var installed = _snapshot.LowLevelDriverInstalled;
        if (installed && _driverWasMissing)
        {
            _driverWasMissing = false;
            _hardware.Restart();
        }
        _driverWasMissing = !installed;

        var show = !installed && (noTemperature || noPower) && !_driverBannerDismissed;
        var panelOpen = DetailsView.Visibility == Visibility.Visible || SettingsView.Visibility == Visibility.Visible;
        HardwareDriverBanner.Visibility = show && !panelOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!show || _driverInstallStarted) return;
        HardwareDriverTitle.Text = noTemperature
            ? LocalizationService.Get("Driver.TitleNoTemperature")
            : LocalizationService.Get("Driver.TitleNoPower");
        HardwareDriverMessage.Text = noTemperature
            ? LocalizationService.Get("Driver.BodyNoTemperature")
            : LocalizationService.Get("Driver.BodyNoPower");
    }

    private void PurgeData_Click(object sender, RoutedEventArgs e)
    {
        if (!_purgeArmed)
        {
            _purgeArmed = true;
            PurgeButton.Content = LocalizationService.Get("Settings.PurgeConfirm");
            PurgeWarning.Visibility = Visibility.Visible;
            return;
        }
        try { StartupService.RemoveRegistration(); } catch (Exception ex) { Log($"Startup cleanup: {ex.Message}"); }
        UninstallLowLevelDriver();
        _purgeRequested = true;
        Close();
    }

    private static void UninstallLowLevelDriver()
    {
        try
        {
            if (HardwareMonitorService.PawnIoUninstallCommand() is not { Length: > 0 } command) return;
            var (file, arguments) = SplitCommand(command);
            Process.Start(new ProcessStartInfo(file) { Arguments = arguments, UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception ex) { Log($"Driver uninstall: {ex.Message}"); }
    }

    private static (string File, string Arguments) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var close = command.IndexOf('"', 1);
            if (close > 0) return (command[1..close], command[(close + 1)..].Trim());
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? (command[..(exe + 4)], command[(exe + 4)..].Trim()) : (command, "");
    }

    private void DismissHardwareDriver_Click(object sender, RoutedEventArgs e)
    {
        _driverBannerDismissed = true;
        HardwareDriverBanner.Visibility = Visibility.Collapsed;
        ApplyResponsiveLayout();
    }

    private const string PawnIoDownloadPage = "https://pawnio.eu";

    private void InstallHardwareDriver_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PawnIoDownloadPage) { UseShellExecute = true });
            InstallHardwareDriverButton.IsEnabled = false;
            _driverInstallStarted = true;
            HardwareDriverTitle.Text = LocalizationService.Get("Driver.Started");
            HardwareDriverMessage.Text = LocalizationService.Get("Driver.StartedBody");
            HardwareDriverHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            HardwareDriverTitle.Text = LocalizationService.Get("Driver.Failed");
            HardwareDriverMessage.Text = ex.Message;
        }
    }

    private void HideMainViews() => MiniView.Visibility = BalancedView.Visibility = CompactView.Visibility = DetailedView.Visibility = Visibility.Collapsed;
    private void ShowCurrentModeBehindPanel()
    {
        HideMainViews();
        (_settings.ViewMode switch { "Mini" => MiniView, "Compact" => CompactView, "Detailed" => DetailedView, _ => BalancedView }).Visibility = Visibility.Visible;
    }

    private void ConfigureDetailsPage()
    {
        _panelOverlayMode = false;
        HideMainViews();
        SettingsView.Visibility = Visibility.Collapsed;
        HardwareDriverBanner.Visibility = Visibility.Collapsed;
        DetailsView.Width = double.NaN;
        DetailsView.MaxWidth = double.PositiveInfinity;
        DetailsView.HorizontalAlignment = HorizontalAlignment.Stretch;
        DetailsView.BorderThickness = new Thickness(0);
    }

    private void ConfigurePanel(Border panel, double pageMaxWidth)
    {
        _panelOverlayMode = ActualWidth >= 1000 && ActualHeight >= 600;
        HardwareDriverBanner.Visibility = Visibility.Collapsed;
        if (_panelOverlayMode)
        {
            ShowCurrentModeBehindPanel();
            panel.Width = Math.Clamp(ActualWidth * .4, 380, 640);
            panel.MaxWidth = 640;
            panel.HorizontalAlignment = HorizontalAlignment.Right;
            panel.BorderThickness = new Thickness(1, 0, 0, 0);
        }
        else
        {
            HideMainViews();
            panel.Width = double.NaN;
            panel.MaxWidth = pageMaxWidth;
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.BorderThickness = new Thickness(0);
        }
    }

    private void MarkMode(Button tab, Button icon, bool selected)
    {
        tab.Tag = selected ? "Selected" : null;
        icon.Foreground = selected ? FindBrush("AccentBrush") : FindBrush("TextQuietBrush");
    }
}
