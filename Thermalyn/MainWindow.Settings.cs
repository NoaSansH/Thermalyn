using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    private void LoadSettingsControls()
    {
        _loadingSettings = true;
        RefreshSettingsUpdateStatus();
        var language = LocalizationService.Normalize(_settings.Language);
        EnglishRadio.IsChecked = language == LocalizationService.English;
        FrenchRadio.IsChecked = language == LocalizationService.French;
        GpuFirstCheck.IsChecked = _settings.GpuFirst; DarkThemeRadio.IsChecked = _settings.Theme != "Light"; LightThemeRadio.IsChecked = _settings.Theme == "Light"; GraphsCheck.IsChecked = _settings.ShowGraphs; FansCheck.IsChecked = _settings.ShowFans;
        CpuPrimaryTemperatureRadio.IsChecked = _settings.CpuPrimaryMetric != "Load"; CpuPrimaryLoadRadio.IsChecked = _settings.CpuPrimaryMetric == "Load";
        GpuPrimaryTemperatureRadio.IsChecked = _settings.GpuPrimaryMetric != "Load"; GpuPrimaryLoadRadio.IsChecked = _settings.GpuPrimaryMetric == "Load";
        CelsiusRadio.IsChecked = _settings.TemperatureUnit != "F"; FahrenheitRadio.IsChecked = _settings.TemperatureUnit == "F"; StartupCheck.IsChecked = _settings.StartWithWindows; TopmostCheck.IsChecked = _settings.AlwaysOnTop; ProcessesCheck.IsChecked = _settings.ShowProcesses;
        RefreshCombo.SelectedIndex = _settings.RefreshSeconds switch { 2 => 1, 5 => 2, _ => 0 }; DriveCountCombo.SelectedIndex = Math.Clamp(_settings.MaxDrives, 1, 4) - 1;
        FillThresholdCombo(HotThresholdCombo, HotThresholds, _settings.HotTemperature);
        FillThresholdCombo(CriticalThresholdCombo, CriticalThresholds, _settings.CriticalTemperature);
        LoadMonitoringControls();
        ValidateThresholds();
        AccentHexText.Text = NormalizeColor(_settings.AccentColor, "#3B9EFF");
        NormalColorText.Text = NormalizeColor(_settings.NormalTemperatureColor, "#3B9EFF"); HotColorText.Text = NormalizeColor(_settings.HotTemperatureColor, "#FFA83B"); CriticalColorText.Text = NormalizeColor(_settings.CriticalTemperatureColor, "#FF5C5C");
        _loadingSettings = false;
    }

    private void FillThresholdCombo(ComboBox combo, IReadOnlyList<int> values, int selected)
    {
        combo.Items.Clear();
        var choices = values.Append(selected).Distinct().Order().ToArray();
        foreach (var celsius in choices)
            combo.Items.Add(new ComboBoxItem { Content = FahrenheitRadio.IsChecked == true ? $"{celsius * 9 / 5d + 32:0} °F" : $"{celsius} °C", Tag = celsius });
        combo.SelectedIndex = Array.IndexOf(choices, selected);
    }

    private static int ThresholdValue(ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: int celsius } ? celsius : fallback;

    private void ValidateThresholds()
    {
        if (HotThresholdCombo.Items.Count == 0 || CriticalThresholdCombo.Items.Count == 0) return;
        var invalid = ThresholdPairs().Any(pair => ThresholdValue(pair.Critical, 95) < ThresholdValue(pair.Hot, 80) + 5);
        SaveSettingsButton.IsEnabled = !invalid;
        foreach (var pair in ThresholdPairs())
        {
            var pairInvalid = ThresholdValue(pair.Critical, 95) < ThresholdValue(pair.Hot, 80) + 5;
            pair.Critical.SetResourceReference(BorderBrushProperty, pairInvalid ? "TempHotBrush" : "BorderStrongBrush");
            pair.Critical.ToolTip = pairInvalid ? LocalizationService.Get("Settings.ThresholdWarning") : null;
        }
        ThresholdWarning.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
        ThresholdWarning.Text = LocalizationService.Get("Settings.ThresholdWarning");
    }

    private void SaveWindowSettings()
    {
        if (WindowState == WindowState.Normal) { _settings.WindowWidth = Width; _settings.WindowHeight = Height; _settings.WindowLeft = Left; _settings.WindowTop = Top; }
        _settingsService.Save(_settings);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _updateCancellation.Cancel();
        _updateCancellation.Dispose();
        _updateService.Dispose();
        _tray?.Dispose();
        _tray = null;
        _timer.Stop();
        if (_purgeRequested) DeleteSettingsFolder(); else { SaveWindowSettings(); _events.Save(); }
        _hardware.Dispose();
        if (_windowSource is not null) _windowSource.RemoveHook(WindowMessageHook);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void CpuCard_Click(object sender, MouseButtonEventArgs e) => ShowDetails(_snapshot.Cpu);
    private void GpuCard_Click(object sender, MouseButtonEventArgs e) => ShowDetails(_snapshot.Gpu);
    private void RamCard_Click(object sender, MouseButtonEventArgs e) => ShowDetails(_snapshot.Memory);
    private void Mini_Click(object sender, RoutedEventArgs e) => ApplyMode("Mini");
    private void Compact_Click(object sender, RoutedEventArgs e) => ApplyMode("Compact");
    private void Balanced_Click(object sender, RoutedEventArgs e) => ApplyMode("Balanced");
    private void Detailed_Click(object sender, RoutedEventArgs e) => ApplyMode("Detailed");
    private void CloseDetails_Click(object sender, RoutedEventArgs e) { ApplyMode(_settings.ViewMode); UpdateHardwareDriverBanner(); }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
        LoadSettingsControls(); DetailsView.Visibility = Visibility.Collapsed; SettingsView.Visibility = Visibility.Visible;
        ConfigurePanel(SettingsView, 640);
        SettingsScroll.ScrollToTop();
        ApplyResponsiveLayout();
        Dispatcher.BeginInvoke(() => SettingsScroll.ScrollToTop(), DispatcherPriority.Loaded);
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
        ApplyMode(_settings.ViewMode);
        UpdateHardwareDriverBanner();
    }

    private void PalettePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag?.ToString() ?? "Studio";
        var palette = Palettes.TryGetValue(name, out var found) ? found : Palettes["Studio"];
        SetDraftPalette(palette.Accent, palette.Normal, palette.Hot, palette.Critical);
    }

    private void SetDraftPalette(string accent, string normal, string hot, string critical)
    {
        AccentHexText.Text = accent; NormalColorText.Text = normal; HotColorText.Text = hot; CriticalColorText.Text = critical;
    }

    private void AccentHexText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        try { AccentPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AccentHexText.Text)); }
        catch { AccentPreview.Background = FindBrush("TempCriticalBrush"); }
    }

    private void TemperatureColorText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        SetColorPreview(NormalColorPreview, NormalColorText.Text); SetColorPreview(HotColorPreview, HotColorText.Text); SetColorPreview(CriticalColorPreview, CriticalColorText.Text);
    }

    private void SetColorPreview(Border preview, string color)
    {
        try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { preview.Background = FindBrush("Surface2Brush"); }
    }

    private void OpenColorPicker_Click(object sender, MouseButtonEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Accent";
        _pickerTarget = tag switch { "Normal" => NormalColorText, "Hot" => HotColorText, "Critical" => CriticalColorText, _ => AccentHexText };
        ColorPickerTitle.Text = tag switch { "Normal" => LocalizationService.Get("Settings.NormalTemperature"), "Hot" => LocalizationService.Get("Settings.HotTemperature"), "Critical" => LocalizationService.Get("Settings.CriticalTemperature"), _ => LocalizationService.Get("Settings.Accent") };
        SetPopupColor(NormalizeColor(_pickerTarget.Text, "#3B9EFF"));
        ColorPickerOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void SetPopupColor(string value)
    {
        try
        {
            _syncingColorWheel = true;
            var color = (Color)ColorConverter.ConvertFromString(value);
            PopupColorWheel.SetColor(color);
            PopupBrightnessSlider.Value = PopupColorWheel.Brightness * 100;
            PopupHexText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            PopupColorPreview.Background = new SolidColorBrush(color);
            UpdateColorWarning(color);
        }
        catch { }
        finally { _syncingColorWheel = false; }
    }

    private void PopupColorWheel_ColorChanged(object? sender, Controls.ColorWheelChangedEventArgs e)
    {
        if (_syncingColorWheel) return;
        _syncingColorWheel = true;
        PopupHexText.Text = $"#{e.Color.R:X2}{e.Color.G:X2}{e.Color.B:X2}";
        PopupColorPreview.Background = new SolidColorBrush(e.Color);
        UpdateColorWarning(e.Color);
        _syncingColorWheel = false;
    }

    private void UpdateColorWarning(Color selected)
    {
        var surface = ((SolidColorBrush)FindBrush("CanvasBrush")).Color;
        var ratio = ContrastRatio(selected, surface);
        ColorPickerWarning.Visibility = ratio < 2.5 ? Visibility.Visible : Visibility.Collapsed;
        ColorPickerWarning.Text = LocalizationService.Get("Picker.LowContrast");
    }

    private static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        var first = Luminance(a); var second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private void PopupBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _syncingColorWheel) return;
        PopupColorWheel.Brightness = e.NewValue / 100d;
    }

    private void PopupHexText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _syncingColorWheel) return;
        SetPopupColor(PopupHexText.Text);
    }

    private void PopupSwatch_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string color }) SetPopupColor(color); }

    private void ApplyPickerColor_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerTarget is not null) _pickerTarget.Text = NormalizeColor(PopupHexText.Text, _pickerTarget.Text);
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void CancelPickerColor_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void ColorPickerBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void ColorPickerCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void ResetColors_Click(object sender, RoutedEventArgs e)
    {
        var studio = Palettes["Studio"];
        SetDraftPalette(studio.Accent, studio.Normal, studio.Hot, studio.Critical);
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        ValidateThresholds();
        if (!SaveSettingsButton.IsEnabled) return;
        SaveMonitoringControls();
        _settings.Theme = LightThemeRadio.IsChecked == true ? "Light" : "Dark"; _settings.ShowGraphs = GraphsCheck.IsChecked == true; _settings.GpuFirst = GpuFirstCheck.IsChecked == true;
        _settings.CpuPrimaryMetric = CpuPrimaryLoadRadio.IsChecked == true ? "Load" : "Temperature"; _settings.GpuPrimaryMetric = GpuPrimaryLoadRadio.IsChecked == true ? "Load" : "Temperature";
        _settings.ShowFans = FansCheck.IsChecked == true; _settings.ShowProcesses = ProcessesCheck.IsChecked == true; _settings.AccentColor = NormalizeColor(AccentHexText.Text, "#3B9EFF");
        _settings.NormalTemperatureColor = NormalizeColor(NormalColorText.Text, "#3B9EFF"); _settings.HotTemperatureColor = NormalizeColor(HotColorText.Text, "#FFA83B"); _settings.CriticalTemperatureColor = NormalizeColor(CriticalColorText.Text, "#FF5C5C");
        _settings.HotTemperature = ThresholdValue(HotThresholdCombo, 80);
        _settings.CriticalTemperature = Math.Max(_settings.HotTemperature + 5, ThresholdValue(CriticalThresholdCombo, 95));
        _settings.TemperatureUnit = FahrenheitRadio.IsChecked == true ? "F" : "C"; _settings.StartWithWindows = StartupCheck.IsChecked == true; _settings.AlwaysOnTop = TopmostCheck.IsChecked == true;
        _settings.RefreshSeconds = int.Parse(((ComboBoxItem)RefreshCombo.SelectedItem).Tag.ToString()!); _settings.MaxDrives = DriveCountCombo.SelectedIndex + 1; Topmost = _settings.AlwaysOnTop; _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        try { StartupService.SetEnabled(_settings.StartWithWindows); } catch { StatusText.Text = LocalizationService.Get("Settings.StartupFailed"); }
        _settings.Language = FrenchRadio.IsChecked == true ? LocalizationService.French : LocalizationService.English;
        LocalizationService.Apply(_settings.Language);
        UpdateCaptionGlyph();
        UpdateTray();
        ApplyTheme(_settings.Theme); SaveWindowSettings(); ApplyMode(_settings.ViewMode); await RenderAsync(false);
    }
}
