// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerDoNotRound = 1;
    private const int DwmCornerRound = 2;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcLButtonDoubleClick = 0x00A3;
    private const int WmLButtonUp = 0x0202;
    private const int WmNcMouseLeave = 0x02A2;
    private const int WmCancelMode = 0x001F;
    private const int WmCaptureChanged = 0x0215;
    private const int HtCaption = 2;
    private const int HtMaxButton = 9;
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouseEventData eventTrack);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(ref NativePoint point);

    // A caption gesture owns its release; resize only after that release is consumed.
    private bool _captionGesturePending;
    private bool _captionDoubleClick;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

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
        UpdateWindowCorners();
        UpdateCaptionGlyph();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is WmCancelMode or WmCaptureChanged)
        {
            _captionGesturePending = false;
            _captionDoubleClick = false;
        }
        else if (_captionGesturePending && message is WmLButtonUp or WmNcLButtonUp)
        {
            var cursor = new NativePoint();
            var toggle = _captionDoubleClick ||
                (GetCursorPos(ref cursor) && IsPointInside(MaximizeCaptionButton, new Point(cursor.X, cursor.Y)));
            _captionGesturePending = false;
            _captionDoubleClick = false;
            handled = true;
            _ = ReleaseCapture();
            SetMaximizeHover(false);
            if (toggle) ToggleMaximize();
        }
        else if (message == WmGetMinMaxInfo)
        {
            ApplyMaximizedWorkArea(hwnd, lParam);
            handled = true;
        }
        else if (message == WmNcHitTest)
        {
            var point = new Point(
                unchecked((short)(lParam.ToInt64() & 0xFFFF)),
                unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF)));
            var overMaximize = IsPointInside(MaximizeCaptionButton, point);
            SetMaximizeHover(overMaximize);
            if (overMaximize)
            {
                BeginNonClientMouseTracking(hwnd);
                handled = true;
                return new IntPtr(HtMaxButton);
            }
        }
        else if (message == WmNcMouseMove)
        {
            var overMaximize = wParam.ToInt32() == HtMaxButton;
            SetMaximizeHover(overMaximize);
            if (overMaximize) BeginNonClientMouseTracking(hwnd);
        }
        else if (message == WmNcMouseLeave)
        {
            SetMaximizeHover(false);
        }
        else if ((message is WmNcLButtonDown or WmNcLButtonDoubleClick && wParam.ToInt32() == HtMaxButton) ||
                 (message == WmNcLButtonDoubleClick && wParam.ToInt32() == HtCaption))
        {
            _captionDoubleClick = wParam.ToInt32() == HtCaption;
            _captionGesturePending = true;
            _ = SetCapture(hwnd);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static bool IsPointInside(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight).Contains(screenPoint);
    }

    private static void BeginNonClientMouseTracking(IntPtr hwnd)
    {
        var tracking = new TrackMouseEventData
        {
            Size = (uint)Marshal.SizeOf<TrackMouseEventData>(),
            Flags = TmeLeave | TmeNonClient,
            Window = hwnd
        };
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

    private static void ApplyMaximizedWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMax.MaxPosition.X = Math.Abs(monitorInfo.Work.Left - monitorInfo.Monitor.Left);
        minMax.MaxPosition.Y = Math.Abs(monitorInfo.Work.Top - monitorInfo.Monitor.Top);
        minMax.MaxSize.X = Math.Abs(monitorInfo.Work.Right - monitorInfo.Work.Left);
        minMax.MaxSize.Y = Math.Abs(monitorInfo.Work.Bottom - monitorInfo.Work.Top);
        Marshal.StructureToPtr(minMax, lParam, false);
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateCaptionGlyph();
        MinimizeToNotificationArea();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateCaptionGlyph()
    {
        if (!IsInitialized) return;
        var maximized = WindowState == WindowState.Maximized;
        UpdateWindowCorners();
        MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeCaptionButton.ToolTip = LocalizationService.Get(maximized ? "Window.Restore" : "Window.Maximize");
        MinimizeCaptionButton.ToolTip = LocalizationService.Get("Window.Minimize");
        CloseCaptionButton.ToolTip = LocalizationService.Get("Window.Close");
    }

    private void UpdateWindowCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var preference = WindowState == WindowState.Maximized ? DwmCornerDoNotRound : DwmCornerRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference, sizeof(int));
    }
}
