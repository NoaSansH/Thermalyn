using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouseEventData eventTrack);

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventData
    {
        public uint Size;
        public uint Flags;
        public IntPtr Window;
        public uint HoverTime;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
        UpdateCaptionGlyph();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            var point = new Point(unchecked((short)(lParam.ToInt64() & 0xFFFF)), unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF)));
            var overMaximize = IsPointInside(MaximizeCaptionButton, point);
            SetMaximizeHover(overMaximize);
            if (overMaximize) { BeginNonClientMouseTracking(hwnd); handled = true; return new IntPtr(HtMaxButton); }
        }
        else if (message == WmNcMouseMove)
        {
            var overMaximize = wParam.ToInt32() == HtMaxButton;
            SetMaximizeHover(overMaximize);
            if (overMaximize) BeginNonClientMouseTracking(hwnd);
        }
        else if (message == WmNcMouseLeave) SetMaximizeHover(false);
        else if (message == WmNcLButtonUp && wParam.ToInt32() == HtMaxButton)
        {
            SetMaximizeHover(false); ToggleMaximize(); handled = true;
        }
        return IntPtr.Zero;
    }

    private static bool IsPointInside(FrameworkElement element, Point point)
    {
        if (!element.IsVisible || element.ActualWidth <= 0) return false;
        var origin = element.PointToScreen(new Point());
        return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)).Contains(point);
    }

    private static void BeginNonClientMouseTracking(IntPtr hwnd)
    {
        var tracking = new TrackMouseEventData { Size = (uint)Marshal.SizeOf<TrackMouseEventData>(), Flags = TmeLeave | TmeNonClient, Window = hwnd };
        _ = TrackMouseEvent(ref tracking);
    }

    private void SetMaximizeHover(bool hovered)
    {
        if (hovered)
        {
            MaximizeCaptionButton.SetResourceReference(Control.BackgroundProperty, "Surface3Brush");
            MaximizeCaptionButton.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        }
        else
        {
            MaximizeCaptionButton.Background = Brushes.Transparent;
            MaximizeCaptionButton.SetResourceReference(Control.ForegroundProperty, "IconBrush");
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void UpdateCaptionGlyph()
    {
        if (!IsInitialized) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeCaptionButton.ToolTip = LocalizationService.Get(maximized ? "Window.Restore" : "Window.Maximize");
        MinimizeCaptionButton.ToolTip = LocalizationService.Get("Window.Minimize");
        CloseCaptionButton.ToolTip = LocalizationService.Get("Window.Close");
    }
}
