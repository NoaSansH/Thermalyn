using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Thermalyn.Models;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    private Geometry Glyph(string key) => (Geometry)FindResource(key);

    private Border ListRow(Geometry? icon, string label, string meta, string value, Brush? valueBrush, bool inactive = false)
    {
        var border = new Border { Tag = "Row" }; border.SetResourceReference(StyleProperty, "Row");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActualWidth < 900 ? 180 : 250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        if (icon is not null) head.Children.Add(new Controls.Icon { Data = icon, Width = 15, Height = 15, StrokeThickness = 1.8, Stroke = FindBrush("IconBrush"), VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBlock { Text = label, Foreground = FindBrush("TextSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(icon is null ? 0 : 10, 0, 12, 0) };
        Grid.SetColumn(name, 1); head.Children.Add(name);
        var middle = new TextBlock { Text = meta, Foreground = FindBrush("TextQuietBrush"), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(middle, 1);
        var right = new TextBlock { Text = value, FontFamily = MonoFont, FontSize = 14, Foreground = valueBrush ?? FindBrush("TextPrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(right, 2);
        grid.Children.Add(head); grid.Children.Add(middle); grid.Children.Add(right);
        border.Child = grid;
        if (inactive) border.Opacity = .5;
        return border;
    }

    private Border ValueRow(string label, string value)
    {
        var border = new Border { Tag = "Row", Padding = new Thickness(18, 10, 18, 10) }; border.SetResourceReference(StyleProperty, "Row");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, Foreground = FindBrush("TextSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var right = new TextBlock { Text = value, FontFamily = MonoFont, FontSize = 13, Foreground = FindBrush("TextPrimaryBrush"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1); grid.Children.Add(right); border.Child = grid; return border;
    }

    private Border EmptyRow(string text)
    {
        var border = new Border { Tag = "Row", Padding = new Thickness(18, 12, 18, 12) }; border.SetResourceReference(StyleProperty, "Row");
        border.Child = new TextBlock { Text = text, Foreground = FindBrush("TextQuietBrush"), FontSize = 11.5, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap };
        return border;
    }

    private static void NormalizeDividers(DependencyObject root)
    {
        var rows = new List<Border>();
        Collect(root, rows);
        for (var i = 0; i < rows.Count; i++) rows[i].BorderThickness = new Thickness(0, 0, 0, i == rows.Count - 1 ? 0 : 1);

        static void Collect(DependencyObject node, List<Border> rows)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                if (child is Border { Tag: "Row" } row) { rows.Add(row); continue; }
                Collect(child, rows);
            }
        }
    }

    private void BuildStorage()
    {
        StoragePanel.Children.Clear();
        foreach (var drive in _snapshot.Storage.Take(_settings.MaxDrives))
        {
            var row = ListRow(Glyph("IconHardDrive"), $"{drive.Kind} · {drive.Name}", "", Temperature(drive.Temperature), TemperatureBrush(drive.Temperature, ThermalComponent.Storage));
            row.Cursor = Cursors.Hand; row.MouseLeftButtonUp += (_, _) => ShowDetails(drive); StoragePanel.Children.Add(row);
        }
    }

    private void BuildFans()
    {
        FanSection.Visibility = _settings.ShowFans ? Visibility.Visible : Visibility.Collapsed; FanPanel.Children.Clear();
        if (_snapshot.Fans.Count == 0)
        {
            FanPanel.Children.Add(new TextBlock { Text = LocalizationService.Get("Empty.NoFanSpeedExceptGpu"), Foreground = FindBrush("TextQuietBrush"), FontSize = 11.5, FontStyle = FontStyles.Italic, TextTrimming = TextTrimming.CharacterEllipsis });
            return;
        }
        FanPanel.Children.Add(new TextBlock
        {
            Text = string.Join("   ·   ", _snapshot.Fans.Take(4).Select(fan => $"{fan.Kind} {Rpm(fan.FanRpm)}")),
            FontFamily = MonoFont,
            FontSize = 12,
            Foreground = FindBrush("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
    }

    private void BuildGpuDetails()
    {
        DetailedGpuPanel.Children.Clear(); BalancedGpuPanel.Children.Clear();
        if (_snapshot.Gpus.Count == 0)
        {
            DetailedGpuPanel.Children.Add(ListRow(Glyph("IconGpu"), LocalizationService.Get("Common.GraphicsCard"), LocalizationService.Get("Empty.NoComponent"), "—", FindBrush("TextQuietBrush")));
            BalancedGpuPanel.Children.Add(ListRow(Glyph("IconGpu"), LocalizationService.Get("Common.GraphicsCard"), LocalizationService.Get("Empty.NoComponent"), "—", FindBrush("TextQuietBrush")));
            NormalizeDividers(DetailedGpuPanel);
            return;
        }
        foreach (var gpu in _snapshot.Gpus)
        {
            var kind = gpu.IsIntegratedGpu ? LocalizationService.Get("Common.IntegratedGraphics") : LocalizationService.Get("Common.GraphicsCard");
            var label = $"{kind} · {gpu.Name} · {LocalizationService.Get(gpu.IsActiveGpu ? "Common.Active" : "Common.Inactive")}";
            var meta = gpu.IsActiveGpu ? $"{LocalizationService.Get("Common.Load")} {Percent(gpu.Load)}" : LocalizationService.Get("Empty.GpuAsleep");
            var value = gpu.IsActiveGpu ? Temperature(gpu.Temperature) : "—";
            var brush = gpu.IsActiveGpu ? TemperatureBrush(gpu.Temperature, ThermalComponent.Gpu) : FindBrush("TextQuietBrush");
            foreach (var panel in new[] { DetailedGpuPanel, BalancedGpuPanel })
            {
                var row = ListRow(Glyph("IconGpu"), label, meta, value, brush, !gpu.IsActiveGpu);
                row.Cursor = Cursors.Hand; row.MouseLeftButtonUp += (_, _) => ShowDetails(gpu); panel.Children.Add(row);
            }
        }
        NormalizeDividers(DetailedGpuPanel);
    }

    private void BuildDetailedSystem()
    {
        DetailedSystemPanel.Children.Clear();
        var memory = ListRow(Glyph("IconMemory"), $"{LocalizationService.Get("Common.Ram")} {MemorySummary(_snapshot.Memory)}".TrimEnd(), _snapshot.Memory.Name, Percent(_snapshot.Memory.Load), FindBrush("TextPrimaryBrush"));
        memory.Cursor = Cursors.Hand; memory.MouseLeftButtonUp += (_, _) => ShowDetails(_snapshot.Memory); DetailedSystemPanel.Children.Add(memory);
        foreach (var battery in _snapshot.Batteries) DetailedSystemPanel.Children.Add(BatteryRow(battery));
        foreach (var drive in _snapshot.Storage.Take(_settings.MaxDrives))
        {
            var row = ListRow(Glyph("IconHardDrive"), $"{drive.Kind} · {drive.Name}", "", Temperature(drive.Temperature), TemperatureBrush(drive.Temperature, ThermalComponent.Storage));
            row.Cursor = Cursors.Hand; row.MouseLeftButtonUp += (_, _) => ShowDetails(drive); DetailedSystemPanel.Children.Add(row);
        }
        if (_settings.ShowFans)
        {
            if (_snapshot.Fans.Count == 0) DetailedSystemPanel.Children.Add(ListRow(Glyph("IconFan"), LocalizationService.Get("Common.Fans"), LocalizationService.Get("Empty.NoFanSpeed"), "—", FindBrush("TextQuietBrush")));
            else foreach (var fan in _snapshot.Fans.Take(4)) DetailedSystemPanel.Children.Add(ListRow(Glyph("IconFan"), $"{LocalizationService.Get("Common.Fan")} · {fan.Kind}", fan.Name, Rpm(fan.FanRpm), FindBrush("TextPrimaryBrush")));
        }
        NormalizeDividers(DetailedSystemPanel);
    }

    private Border BatteryRow(BatteryReading battery)
    {
        var meta = new List<string> { LocalizationService.Get(battery.IsCharging ? "Battery.Charging" : "Battery.Discharging") };
        if (battery.RateWatts is > .05) meta.Add($"{battery.RateWatts:0.0} W");
        if (battery.RemainingTime is { } left) meta.Add(LocalizationService.Format("Battery.TimeLeft", Duration(left)));
        if (battery.HealthPercent is { } health) meta.Add($"{LocalizationService.Get("Battery.Health")} {health:0}%");
        return ListRow(Glyph("IconBattery"), LocalizationService.Get("Common.Battery"),
            string.Join(" · ", meta), Percent(battery.ChargePercent), FindBrush("TextPrimaryBrush"));
    }

    private static string Duration(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours} h {value.Minutes:00}"
        : $"{(int)value.TotalMinutes} min";

    private void BuildDetailedSensorPanels()
    {
        BuildSensorSummary(DetailedCpuSensorsPanel, _snapshot.Cpu);
        BuildSensorSummary(DetailedGpuSensorsPanel, _snapshot.Gpu);
    }

    private void BuildSensorSummary(StackPanel panel, ComponentReading reading)
    {
        panel.Children.Clear();
        var usable = reading.Details.Where(IsUserFacingSensor).Where(sensor => _settings.ShowFans || sensor.Type != "Fan").ToList();
        var summary = usable.Where(sensor => !IsPerInstanceSensor(sensor.Name)).ToList();
        if (summary.Count == 0) summary = usable;
        var sensors = summary.OrderBy(SensorRank).ThenBy(sensor => SensorOrdinal(sensor.Name)).ThenBy(sensor => sensor.Name)
            .DistinctBy(sensor => (FriendlySensorName(sensor.Name), sensor.Type)).Take(8).ToList();
        if (sensors.Count == 0) { panel.Children.Add(EmptyRow(LocalizationService.Get("Empty.NoExtraSensors"))); NormalizeDividers(panel); return; }
        foreach (var sensor in sensors) panel.Children.Add(ValueRow(FriendlySensorName(sensor.Name), SensorValue(sensor)));
        NormalizeDividers(panel);
    }

    private void BuildProcessRankings()
    {
        BuildProcessPanel(CpuProcessesPanel, _processSnapshot.OrderByDescending(p => p.CpuPercent).Take(5), p => $"{p.CpuPercent:0.0}%");
        BuildProcessPanel(RamProcessesPanel, _processSnapshot.OrderByDescending(p => p.MemoryMb).Take(5), p => MemorySize(p.MemoryMb));
        var gpu = _processSnapshot.Where(p => p.GpuPercent > .05).OrderByDescending(p => p.GpuPercent).Take(5).ToList();
        BuildProcessPanel(GpuProcessesPanel, gpu, p => $"{p.GpuPercent:0.0}%", LocalizationService.Get("Empty.NoGpuActivity"));
    }

    private static string MemorySize(double megabytes) => megabytes >= 1024
        ? $"{megabytes / 1024:0.0} " + LocalizationService.Get("Unit.Gigabytes")
        : $"{megabytes:0} " + LocalizationService.Get("Unit.Megabytes");

    private void BuildProcessPanel(StackPanel panel, IEnumerable<ProcessUsage> processes, Func<ProcessUsage, string> value, string? empty = null)
    {
        empty ??= LocalizationService.Get("Empty.NoData");
        panel.Children.Clear(); var list = processes.ToList();
        if (list.Count == 0) { panel.Children.Add(EmptyRow(empty)); NormalizeDividers(panel); return; }
        for (var i = 0; i < list.Count; i++) panel.Children.Add(ValueRow($"{i + 1}. {list[i].Name}", value(list[i])));
        NormalizeDividers(panel);
    }

    private void ShowDetails(ComponentReading reading)
    {
        var name = reading.Name;
        var kind = reading.Kind;
        _detailReader = ReferenceEquals(reading, _snapshot.Cpu) || reading.Kind == "CPU"
            ? () => _snapshot.Cpu
            : ReferenceEquals(reading, _snapshot.Memory) || reading.Kind == "RAM"
                ? () => _snapshot.Memory
                : ReferenceEquals(reading, _snapshot.Gpu) || _snapshot.Gpus.Contains(reading) || reading.Kind == "GPU"
                    ? () => _snapshot.Gpus.FirstOrDefault(gpu => gpu.Name == name) ?? _snapshot.Gpu
                    : () => _snapshot.Storage.FirstOrDefault(drive => drive.Kind == kind && drive.Name == name);
        _detailComponent = ComponentKind(reading);
        DetailKind.Text = reading.Kind; DetailName.Text = reading.Name; DetailSummaryGrid.Children.Clear(); DetailSensors.Children.Clear();
        _detailHistory = ReferenceEquals(reading, _snapshot.Cpu) ? _cpuHistory : ReferenceEquals(reading, _snapshot.Gpu) ? _gpuHistory :
            ReferenceEquals(reading, _snapshot.Memory) ? _ramHistory : null;
        _detailShowsTemperature = !ReferenceEquals(reading, _snapshot.Memory);
        DetailChartLabel.Text = _detailShowsTemperature ? LocalizationService.Get("Detail.TemperatureHistory") : LocalizationService.Get("Detail.MemoryHistory");
        DetailChartCard.Visibility = _detailHistory is null || !_settings.ShowGraphs ? Visibility.Collapsed : Visibility.Visible;
        var temperatureBrush = ReferenceEquals(reading, _snapshot.Memory)
            ? FindBrush("TextSecondaryBrush")
            : TemperatureBrush(reading.Temperature, ComponentKind(reading));
        AddDetailSummary(LocalizationService.Get("Common.Temperature"), Temperature(reading.Temperature), temperatureBrush);
        AddDetailSummary(LocalizationService.Get("Common.Load"), Percent(reading.Load));
        if (_settings.ShowFans) AddDetailSummary(LocalizationService.Get("Common.Fan"), Rpm(reading.FanRpm));
        AddDetailSummary(LocalizationService.Get("Common.Frequency"), Mhz(reading.ClockMhz)); AddDetailSummary(LocalizationService.Get("Common.Power"), Watts(reading.PowerWatts));
        if (reading.Details.Count > 0)
        {
            var usefulSensors = reading.Details.Where(IsUserFacingSensor).Where(sensor => _settings.ShowFans || sensor.Type != "Fan").ToList();
            foreach (var group in usefulSensors.GroupBy(sensor => SensorGroup(sensor.Type)))
                AddSensorGroup(group.Key, group.OrderBy(SensorRank).ThenBy(sensor => SensorOrdinal(sensor.Name)).ThenBy(sensor => sensor.Name)
                    .Take(24).Select(sensor => (FriendlySensorName(sensor.Name), SensorValue(sensor))));
        }
        if (ReferenceEquals(reading, _snapshot.Memory) && _processSnapshot.Count > 0)
            AddSensorGroup(LocalizationService.Get("Detail.TopMemoryApps"), _processSnapshot.OrderByDescending(p => p.MemoryMb).Take(8)
                .Select(process => (process.Name, MemorySize(process.MemoryMb))));
        DetailsView.Visibility = Visibility.Visible;
        ConfigureDetailsPage(); ApplyResponsiveLayout(); RedrawDetailChart();
    }

    private static int SensorOrdinal(string name)
    {
        var hash = name.LastIndexOf('#');
        if (hash < 0) return int.MaxValue;
        var digits = new string(name[(hash + 1)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var ordinal) ? ordinal : int.MaxValue;
    }

    private static bool IsPerInstanceSensor(string name) => SensorOrdinal(name) != int.MaxValue;

    private static int SensorRank(SensorReading sensor)
    {
        var type = sensor.Type switch { "Temperature" => 0, "Load" => 1, "Clock" => 2, "Power" => 3, "Fan" => 4, _ => 5 };
        var aggregate = sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)
            || sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
            || sensor.Name.Contains("Average", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        return type * 2 + aggregate;
    }

    private static readonly string[] TechnicalSensorTerms =
        ["D3D", "Compute", "Copy", "Video Decode", "Video Encode", "PCIe", "Bus Speed", "Effective Clock"];

    private static bool IsUserFacingSensor(SensorReading sensor)
    {
        var name = sensor.Name;
        if (TechnicalSensorTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase))) return false;
        if (sensor.Type == "Status") return false;
        if (sensor.Type == "Temperature" && sensor.Value is <= 0 or >= 125) return false;
        if ((sensor.Type is "Power" or "Clock" or "Fan") && Math.Abs(sensor.Value) < .05) return false;
        return true;
    }

    private static string FriendlySensorName(string name) => name
        .Replace("CPU Total", LocalizationService.Get("Sensor.Name.CpuTotal"), StringComparison.OrdinalIgnoreCase)
        .Replace("GPU Core", LocalizationService.Get("Sensor.Name.GpuCore"), StringComparison.OrdinalIgnoreCase)
        .Replace("Core Average", LocalizationService.Get("Sensor.Name.CoreAverage"), StringComparison.OrdinalIgnoreCase)
        .Replace("GPU Memory", LocalizationService.Get("Sensor.Name.GpuMemory"), StringComparison.OrdinalIgnoreCase)
        .Replace("Memory Controller", LocalizationService.Get("Sensor.Name.MemoryController"), StringComparison.OrdinalIgnoreCase)
        .Replace("Hot Spot", LocalizationService.Get("Sensor.Name.HotSpot"), StringComparison.OrdinalIgnoreCase)
        .Replace("Package", LocalizationService.Get("Sensor.Name.Package"), StringComparison.OrdinalIgnoreCase);

    private string SensorValue(SensorReading sensor) => sensor.Type == "Status" ? sensor.Unit : sensor.Unit == "°C" ? Temperature(sensor.Value) : $"{sensor.Value:0.#} {sensor.Unit}";
    private static string SensorGroup(string type) => type switch
    {
        "Temperature" => LocalizationService.Get("Sensor.Group.Temperatures"),
        "Power" => LocalizationService.Get("Sensor.Group.Power"),
        "Clock" => LocalizationService.Get("Sensor.Group.Clocks"),
        "Fan" => LocalizationService.Get("Sensor.Group.Cooling"),
        "Load" => LocalizationService.Get("Common.Load"),
        "Data" => LocalizationService.Get("Common.Memory"),
        _ => LocalizationService.Get("Sensor.Group.Other")
    };

    private void RedrawDetailChart()
    {
        if (_detailHistory is null || DetailChartCard.Visibility != Visibility.Visible) return;
        var data = _detailHistory.ToArray();
        string FormatValue(double value) => _detailShowsTemperature ? Temperature(value) : $"{value:0}%";
        var stroke = _detailShowsTemperature && data.Length > 0 ? TemperatureBrush(data[^1], _detailComponent) : FindBrush("AccentBrush");
        UpdateChart(DetailChartHistory, data, _detailShowsTemperature, stroke, DetailChartLabel.Text);
        if (data.Length == 0) { DetailChartCurrent.Text = DetailChartHistory.LeftCaption = DetailChartHistory.RightCaption = "—"; return; }
        DetailChartCurrent.Text = FormatValue(data[^1]);
        DetailChartCurrent.Foreground = stroke;
        DetailChartHistory.LeftCaption = $"Min. {FormatValue(data.Min())}";
        DetailChartHistory.RightCaption = $"Max. {FormatValue(data.Max())}";
    }

    private void AddSensorGroup(string title, IEnumerable<(string Label, string Value)> rows)
    {
        var heading = new TextBlock { Text = title.ToUpperInvariant() }; heading.SetResourceReference(StyleProperty, "SectionTitle");
        DetailSensors.Children.Add(heading);
        var panel = new StackPanel();
        foreach (var (label, value) in rows) panel.Children.Add(ValueRow(label, value));
        NormalizeDividers(panel);
        var card = new Border { Child = panel }; card.SetResourceReference(StyleProperty, "ListCard");
        DetailSensors.Children.Add(card);
    }

    private void AddDetailSummary(string label, string value, Brush? brush = null)
    {
        if (value == "—") brush ??= FindBrush("TextQuietBrush");
        var border = new Border { Margin = new Thickness(0, 0, 24, 0) }; border.SetResourceReference(StyleProperty, "FlatCard");
        var stack = new StackPanel();
        var caption = new TextBlock { Text = label }; caption.SetResourceReference(StyleProperty, "FieldLabel");
        stack.Children.Add(caption);
        stack.Children.Add(new TextBlock { Text = value, FontFamily = MonoFont, FontSize = 15, Foreground = brush ?? FindBrush("TextSecondaryBrush") });
        border.Child = stack; DetailSummaryGrid.Children.Add(border);
    }
}
