using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Thermalyn.Controls;

public sealed class ColorWheelChangedEventArgs(Color color) : EventArgs
{
    public Color Color { get; } = color;
}

public sealed class ColorWheel : FrameworkElement
{
    private double _hue;
    private double _saturation = 1;
    private double _brightness = 1;
    private WriteableBitmap? _bitmap;
    private Size _bitmapSize;

    public event EventHandler<ColorWheelChangedEventArgs>? ColorChanged;

    public double Brightness
    {
        get => _brightness;
        set
        {
            var next = Math.Clamp(value, .08, 1);
            if (Math.Abs(next - _brightness) < .001) return;
            _brightness = next;
            _bitmap = null;
            InvalidateVisual();
            RaiseColorChanged();
        }
    }

    public Color SelectedColor => HsvToColor(_hue, _saturation, _brightness);

    public void SetColor(Color color)
    {
        RgbToHsv(color, out _hue, out _saturation, out _brightness);
        _bitmap = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var side = Math.Max(1, (int)Math.Min(ActualWidth, ActualHeight));
        if (_bitmap is null || _bitmapSize.Width != side || _bitmapSize.Height != side)
        {
            _bitmap = BuildWheel(side);
            _bitmapSize = new Size(side, side);
        }
        var left = (ActualWidth - side) / 2;
        var top = (ActualHeight - side) / 2;
        drawingContext.DrawImage(_bitmap, new Rect(left, top, side, side));

        var radius = side / 2d - 2;
        var angle = _hue * Math.PI / 180;
        var point = new Point(left + side / 2d + Math.Cos(angle) * radius * _saturation,
            top + side / 2d + Math.Sin(angle) * radius * _saturation);
        drawingContext.DrawEllipse(null, new Pen(Brushes.White, 2.5), point, 6, 6);
        drawingContext.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 1), point, 8, 8);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(this));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void Pick(Point point)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var radius = Math.Max(1, side / 2 - 2);
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > radius) return;
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Clamp(distance / radius, 0, 1);
        InvalidateVisual();
        RaiseColorChanged();
    }

    private void RaiseColorChanged() => ColorChanged?.Invoke(this, new ColorWheelChangedEventArgs(SelectedColor));

    private WriteableBitmap BuildWheel(int side)
    {
        var bitmap = new WriteableBitmap(side, side, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[side * side * 4];
        var center = (side - 1) / 2d;
        var radius = Math.Max(1, side / 2d - 2);
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius) continue;
                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var color = HsvToColor(hue, Math.Clamp(distance / radius, 0, 1), _brightness);
                var index = (y * side + x) * 4;
                pixels[index] = color.B; pixels[index + 1] = color.G; pixels[index + 2] = color.R; pixels[index + 3] = 255;
            }
        bitmap.WritePixels(new Int32Rect(0, 0, side, side), pixels, side * 4, 0);
        return bitmap;
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60d) % 2 - 1));
        var m = value - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }
}
