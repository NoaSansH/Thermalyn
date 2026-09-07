using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Thermalyn.Services;

namespace Thermalyn.Controls;

// Samples keep their source unit. Only the scale is derived from them.
public sealed class HistoryChart : FrameworkElement
{
    private const double TopInset = .115;
    private const double BottomInset = .06;
    private const double AxisWidth = 32;
    private const double TimeHeight = 26;
    private static readonly double[] TemperatureTicks = [90, 70, 50, 30];
    private static readonly double[] LoadTicks = [100, 75, 50, 25];
    private static readonly Typeface AxisTypeface = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface MetaTypeface = new(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private int? _markerIndex;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(IEnumerable), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsTemperatureProperty = DependencyProperty.Register(
        nameof(IsTemperature), typeof(bool), typeof(HistoryChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(HistoryChart), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LeftCaptionProperty = DependencyProperty.Register(
        nameof(LeftCaption), typeof(string), typeof(HistoryChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CenterCaptionProperty = DependencyProperty.Register(
        nameof(CenterCaption), typeof(string), typeof(HistoryChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RightCaptionProperty = DependencyProperty.Register(
        nameof(RightCaption), typeof(string), typeof(HistoryChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(HistoryChart),
        new FrameworkPropertyMetadata("°C", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SampleIntervalSecondsProperty = DependencyProperty.Register(
        nameof(SampleIntervalSeconds), typeof(int), typeof(HistoryChart), new PropertyMetadata(1));

    public IEnumerable? Data
    {
        get => (IEnumerable?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public bool IsTemperature
    {
        get => (bool)GetValue(IsTemperatureProperty);
        set => SetValue(IsTemperatureProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string LeftCaption
    {
        get => (string)GetValue(LeftCaptionProperty);
        set => SetValue(LeftCaptionProperty, value);
    }

    public string CenterCaption
    {
        get => (string)GetValue(CenterCaptionProperty);
        set => SetValue(CenterCaptionProperty, value);
    }

    public string RightCaption
    {
        get => (string)GetValue(RightCaptionProperty);
        set => SetValue(RightCaptionProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public int SampleIntervalSeconds
    {
        get => (int)GetValue(SampleIntervalSecondsProperty);
        set => SetValue(SampleIntervalSecondsProperty, value);
    }

    public double PlotHeight => Math.Max(1, ActualHeight - TimeHeight);

    public HistoryChart()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ToolTip = new ToolTip
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
            PlacementTarget = this,
            StaysOpen = true,
            IsOpen = false
        };
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    private static string Caption(string value, string key) =>
        string.IsNullOrEmpty(value) ? LocalizationService.Get(key) : value;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var data = Values();
        var (min, max, ticks) = Scale(data, IsTemperature);
        var plotWidth = Math.Max(1, ActualWidth - AxisWidth);
        var plotHeight = PlotHeight;
        var gridBrush = ResourceBrush("BorderSubtleBrush", Brushes.DimGray);
        var textBrush = ResourceBrush("TextQuietBrush", Brushes.Gray);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var tick in ticks)
        {
            var y = MapY(tick, min, max, plotHeight);
            drawingContext.DrawLine(new Pen(gridBrush, 1), new Point(AxisWidth, y), new Point(ActualWidth, y));
            DrawText(drawingContext, IsTemperature ? $"{DisplayValue(tick):0}°" : $"{tick:0}%", AxisTypeface, 9.5, textBrush, new Point(0, y - 6), pixelsPerDip);
        }

        if (data.Length > 0)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var i = 0; i < data.Length; i++)
                {
                    var point = new Point(AxisWidth + (data.Length == 1 ? 0 : i * plotWidth / (data.Length - 1)), MapY(data[i], min, max, plotHeight));
                    if (i == 0) context.BeginFigure(point, false, false);
                    else context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(null, new Pen(Stroke, 1.6), geometry);

            if (_markerIndex is int markerIndex && markerIndex >= 0 && markerIndex < data.Length)
            {
                var x = AxisWidth + (data.Length == 1 ? 0 : markerIndex * plotWidth / (data.Length - 1));
                var y = MapY(data[markerIndex], min, max, plotHeight);
                var guidePen = new Pen(Stroke, 1) { DashStyle = DashStyles.Dot };
                drawingContext.DrawLine(guidePen, new Point(x, 0), new Point(x, plotHeight));
                drawingContext.DrawEllipse(Stroke, new Pen(ResourceBrush("CanvasBrush", Brushes.Black), 2), new Point(x, y), 4.5, 4.5);
            }
        }

        var timeY = plotHeight + 4;
        DrawText(drawingContext, Caption(LeftCaption, "Chart.Minus15"), MetaTypeface, 10.5, textBrush, new Point(AxisWidth, timeY), pixelsPerDip);
        DrawAlignedText(drawingContext, Caption(CenterCaption, "Chart.Minus7"), HorizontalAlignment.Center, timeY, textBrush, pixelsPerDip);
        DrawAlignedText(drawingContext, Caption(RightCaption, "Chart.Now"), HorizontalAlignment.Right, timeY, textBrush, pixelsPerDip);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var data = Values();
        if (data.Length == 0 || ToolTip is not ToolTip tip) return;
        var position = e.GetPosition(this);
        if (position.X < AxisWidth || position.Y > PlotHeight)
        {
            tip.IsOpen = false;
            InvalidateVisual();
            return;
        }

        var ratio = Math.Clamp((position.X - AxisWidth) / Math.Max(1, ActualWidth - AxisWidth), 0, 1);
        var index = (int)Math.Round(ratio * (data.Length - 1));
        var ageSeconds = Math.Max(0, data.Length - 1 - index) * Math.Max(1, SampleIntervalSeconds);
        var age = ageSeconds == 0 ? LocalizationService.Get("Chart.Now") : ageSeconds < 60 ? LocalizationService.Format("Chart.AgeSeconds", ageSeconds)
            : LocalizationService.Format("Chart.AgeMinutes", ageSeconds / 60, $"{ageSeconds % 60:00}");
        tip.Content = $"{Label}\n{DisplayValue(data[index]):0.#} {Unit} · {age}";
        tip.IsOpen = true;

        _markerIndex = index;
        InvalidateVisual();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (ToolTip is ToolTip tip) tip.IsOpen = false;
        _markerIndex = null;
        InvalidateVisual();
    }

    private double[] Values() => Data?.Cast<object>().Select(value => Convert.ToDouble(value, CultureInfo.InvariantCulture)).ToArray() ?? [];

    private static double MapY(double value, double min, double max, double height)
    {
        var top = height * TopInset;
        var bottom = height * (1 - BottomInset);
        return bottom - ((value - min) / Math.Max(1, max - min) * (bottom - top));
    }

    private static (double Min, double Max, double[] Ticks) Scale(IReadOnlyCollection<double> data, bool temperature)
    {
        if (!temperature) return (0, 100, LoadTicks);
        if (data.Count == 0) return (30, 90, TemperatureTicks);
        var maximum = Math.Max(90, Math.Ceiling(data.Max() / 10) * 10);
        var minimum = maximum - 60;
        if (data.Min() < minimum) minimum = Math.Max(0, maximum - 90);
        var step = (maximum - minimum) / 3;
        return (minimum, maximum, [maximum, maximum - step, maximum - step * 2, minimum]);
    }

    private double DisplayValue(double value) => IsTemperature && Unit == "°F" ? value * 9 / 5 + 32 : value;

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static void DrawText(DrawingContext context, string text, Typeface typeface, double size, Brush brush, Point origin, double pixelsPerDip) =>
        context.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, pixelsPerDip), origin);

    private void DrawAlignedText(DrawingContext context, string text, HorizontalAlignment alignment, double y, Brush brush, double pixelsPerDip)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, MetaTypeface, 10.5, brush, pixelsPerDip);
        var x = alignment == HorizontalAlignment.Center ? AxisWidth + (ActualWidth - AxisWidth - formatted.Width) / 2 : ActualWidth - formatted.Width;
        context.DrawText(formatted, new Point(x, y));
    }
}
