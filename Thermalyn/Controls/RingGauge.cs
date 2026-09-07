using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Thermalyn.Services;

namespace Thermalyn.Controls;

public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HasValueProperty = DependencyProperty.Register(nameof(HasValue), typeof(bool), typeof(RingGauge),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(RingGauge),
        new FrameworkPropertyMetadata("°", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(RingGauge),
        new FrameworkPropertyMetadata(LocalizationService.Get("Common.Temperature"), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool HasValue { get => (bool)GetValue(HasValueProperty); set => SetValue(HasValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var size = Math.Min(ActualWidth, ActualHeight);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(12, size / 2 - 12);
        var trackThickness = Math.Clamp(size * .026, 2.4, 4.6);
        var valueThickness = Math.Clamp(size * .044, 4, 7.4);
        const double startAngle = -220;
        const double totalSweep = 260;

        drawingContext.PushOpacity(.62);
        DrawArc(drawingContext, center, radius, startAngle, totalSweep, new Pen(Track, trackThickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });
        drawingContext.Pop();
        var maximum = Math.Max(1, Maximum);
        var progressSweep = Math.Clamp(Value, 0, maximum) / maximum * totalSweep;
        if (HasValue && progressSweep > .25)
            DrawArc(drawingContext, center, radius, startAngle, progressSweep, new Pen(Accent, valueThickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });

        var valueText = HasValue ? $"{Value:0}{Unit}" : "—";
        var valueSize = Math.Clamp(size * .18, 18, 32);
        DrawCentered(drawingContext, valueText, valueSize, FontWeights.SemiBold, HasValue ? Accent : TextBrush, center.X, center.Y - valueSize * .66, dpi, HasValue ? 1 : .62, "Cascadia Mono");
        DrawCentered(drawingContext, Label, Math.Clamp(size * .073, 9, 12), FontWeights.Normal, TextBrush, center.X, center.Y + valueSize * .52, dpi, .66, "Segoe UI Variable Text");
    }

    private static void DrawArc(DrawingContext drawingContext, Point center, double radius, double start, double sweep, Pen pen)
    {
        static Point P(Point c, double r, double a) => new(c.X + r * Math.Cos(a * Math.PI / 180), c.Y + r * Math.Sin(a * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(P(center, radius, start), false, false);
            ctx.ArcTo(P(center, radius, start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static void DrawCentered(DrawingContext drawingContext, string text, double size, FontWeight weight, Brush brush, double x, double y, double dpi, double opacity = 1, string fontFamily = "Segoe UI Variable Display")
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal), size, brush, dpi);
        drawingContext.PushOpacity(opacity);
        drawingContext.DrawText(ft, new Point(x - ft.Width / 2, y));
        drawingContext.Pop();
    }
}
