using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Thermalyn.Controls;

// WPF scrolls a fixed number of lines per wheel event and ignores how far the wheel actually
// turned. A mouse sends one event of 120 per notch, so that matches. A precision touchpad sends a
// stream of much smaller deltas, and treating each one as a full notch makes a two-finger swipe
// jump several lines at a time.
internal static class PrecisionScrolling
{
    private const double LineHeight = 16;
    private static bool _enabled;

    internal static void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || sender is not ScrollViewer viewer) return;
        if (viewer.ScrollableHeight <= 0) return;

        // -1 asks for a page per notch, which ScrollViewer already does on its own.
        var lines = SystemParameters.WheelScrollLines;
        if (lines <= 0) return;

        var distance = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine * lines * LineHeight;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - distance);
        e.Handled = true;
    }
}
