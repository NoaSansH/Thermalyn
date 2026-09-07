using Thermalyn.Services;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Thermalyn;

public partial class MainWindow
{
    private void ApplyTheme(string theme)
    {
        _settings.Theme = theme; var light = theme == "Light";
        var canvas = light ? "#F4F3F0" : "#0D0E10"; var surface1 = light ? "#FFFFFF" : "#17191C"; var surface2 = light ? "#E9E7E2" : "#1F2227"; var surface3 = light ? "#DBD8D2" : "#2A2E34";
        var border = light ? "#DCD9D2" : "#23262B"; var borderStrong = light ? "#BFBAB1" : "#31353C";
        var primary = light ? "#101215" : "#F4F5F7"; var secondary = light ? "#33373C" : "#D6D9DE"; var tertiary = light ? "#5E646B" : "#9BA1AA"; var quiet = light ? "#7C828A" : "#727880"; var icon = light ? "#6E747C" : "#8E949D";
        var accent = NormalizeColor(_settings.AccentColor, light ? "#1F6FB5" : "#3B9EFF"); var normal = NormalizeColor(_settings.NormalTemperatureColor, "#3B9EFF"); var hot = NormalizeColor(_settings.HotTemperatureColor, "#FFA83B"); var critical = NormalizeColor(_settings.CriticalTemperatureColor, "#FF5C5C");
        SetBrush("CanvasBrush", canvas); SetBrush("Surface1Brush", surface1); SetBrush("Surface2Brush", surface2); SetBrush("Surface3Brush", surface3); SetBrush("BorderSubtleBrush", border); SetBrush("BorderStrongBrush", borderStrong);
        SetBrush("TextPrimaryBrush", primary); SetBrush("TextSecondaryBrush", secondary); SetBrush("TextTertiaryBrush", tertiary); SetBrush("TextQuietBrush", quiet); SetBrush("IconBrush", icon); SetBrush("MeterBrush", icon);
        SetBrush("AccentBrush", accent); SetBrush("AccentDeepBrush", Darken(accent, .55)); SetBrush("AccentOnBrush", ContrastText(accent)); SetBrush("FocusBrush", accent);
        SetBrush("TempNormalBrush", normal); SetBrush("TempHotBrush", hot); SetBrush("TempCriticalBrush", critical); SetBrush("TrackBrush", border); SetBrush("ScrimBrush", light ? "#800B0D10" : "#B3000000");
        SetBrush("BackgroundBrush", canvas); SetBrush("SurfaceBrush", surface1); SetBrush("SurfaceAltBrush", surface2); SetBrush("BorderBrush", border); SetBrush("TextBrush", primary); SetBrush("MutedBrush", tertiary);
        SetBrush("GoodBrush", normal); SetBrush("WarningBrush", hot); SetBrush("CriticalBrush", critical);
        ApplyWindowFrameTheme(light, canvas);
        if (IsLoaded) ApplyMode(_settings.ViewMode);
    }

    // Same hue, darker: the logo's inner stroke.
    private static string Darken(string color, double factor)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        return $"#{(byte)(c.R * factor):X2}{(byte)(c.G * factor):X2}{(byte)(c.B * factor):X2}";
    }

    private static string ContrastText(string color)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        var luminance = (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255d;
        return luminance > .58 ? "#101214" : "#FFFFFF";
    }

    private void ApplyWindowFrameTheme(bool light, string borderColor)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = light ? 0 : 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var color = (Color)ColorConverter.ConvertFromString(borderColor);
        var colorRef = color.R | color.G << 8 | color.B << 16;
        _ = DwmSetWindowAttribute(hwnd, 34, ref colorRef, sizeof(int));
    }

    private static void SetBrush(string key, string hex) => Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static string NormalizeColor(string? value, string fallback)
    {
        try { _ = (Color)ColorConverter.ConvertFromString(value ?? ""); return value!; } catch { return fallback; }
    }
    private Brush FindBrush(string key) => (Brush)FindResource(key);
    private Brush TemperatureBrush(double? value, ThermalComponent component = ThermalComponent.Cpu) => value is null ? FindBrush("TextQuietBrush")
        : value >= ThermalThresholds.For(_settings, component).Critical ? FindBrush("TempCriticalBrush")
        : value >= ThermalThresholds.For(_settings, component).Hot ? FindBrush("TempHotBrush") : FindBrush("TempNormalBrush");
    private double DisplayTemperatureValue(double? c) => c is null ? 0 : _settings.TemperatureUnit == "F" ? c.Value * 9 / 5 + 32 : c.Value;
    private string Temperature(double? c) => c is null ? "—" : _settings.TemperatureUnit == "F" ? $"{c * 9 / 5 + 32:0} °F" : $"{c:0} °C";
    private static string Percent(double? value) => value is double v ? $"{v:0}%" : "—";
    private static string Rpm(double? value) => value is double v ? $"{v:0} rpm" : "—";
    private static string Mhz(double? value) => value is double v ? v >= 1000 ? $"{v / 1000:0.0} GHz" : $"{v:0} MHz" : "—";
    private static string Watts(double? value) => value is double v ? $"{v:0.0} W" : "—";
}
