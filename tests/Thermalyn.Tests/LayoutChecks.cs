using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Thermalyn;
using Thermalyn.Models;
using Thermalyn.Services;

internal static class LayoutChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RenderMatrix(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Layout checks failed.", failure);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RenderMatrix()
    {
        var output = Path.Combine(Path.GetTempPath(), "Thermalyn-layout");
        Directory.CreateDirectory(output);
        var app = new App(true) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow(true) { ShowInTaskbar = false, Left = 0, Top = 0 };
        window.Show(); Pump();
        if (Environment.GetCommandLineArgs().Contains("--tray-smoke"))
        {
            using var tray = new TrayService(window, () => window.Show(), window.Close);
            if (!tray.Update("Thermalyn · UI test")) throw new InvalidOperationException("Windows rejected the notification area icon.");
            if (!tray.Update("Thermalyn · CPU 72 °C · GPU 64 °C")) throw new InvalidOperationException("Notification area update failed.");
            Console.WriteLine("Notification area: native icon creation, tooltip update and cleanup passed.");
        }
        var count = 0;
        foreach (var (width, height) in new[] { (320, 480), (390, 844), (560, 720), (820, 600), (1200, 820), (1920, 1080) })
            foreach (var (language, theme) in new[] { ("fr", "Dark"), ("en", "Light") })
                foreach (var page in new[] { "Balanced", "Compact", "Detailed", "Details", "Settings" })
                {
                    window.Width = width; window.Height = height; Pump();
                    window.PreparePreviewAsync(new AppSettings
                    {
                        Language = language,
                        Theme = theme,
                        ViewMode = page is "Settings" or "Details" ? "Balanced" : page,
                        ThermalNotifications = true
                    }, page).GetAwaiter().GetResult();
                    Pump(); window.UpdateLayout();
                    var name = $"{width}x{height}-{language}-{page}";
                    if (page == "Settings")
                    {
                        var scroll = (ScrollViewer)window.FindName("SettingsScroll");
                        var threshold = (FrameworkElement)window.FindName("HotThresholdCombo");
                        threshold.BringIntoView(); Pump(); window.UpdateLayout();
                        AssertWithin((FrameworkElement)scroll.Content, scroll, name);
                        foreach (var control in new[] { "HotThresholdCombo", "GpuHotThresholdCombo", "StorageHotThresholdCombo", "AlertDelayCombo", "AlertCooldownCombo" })
                        {
                            var element = (FrameworkElement)window.FindName(control);
                            if (element.ActualWidth < 70) throw new InvalidOperationException($"{name}: {control} too narrow ({element.ActualWidth}).");
                        }
                        Save(window, Path.Combine(output, name + ".png"));
                        scroll.ScrollToBottom(); Pump();
                        Save(window, Path.Combine(output, name + "-bottom.png"));
                    }
                    else Save(window, Path.Combine(output, name + ".png"));
                    count++;
                }
        // Stress the narrow cards with longer values and the independent threshold settings.
        window.Width = 320; window.Height = 600; Pump();
        var stress = new AppSettings { Language = "fr", TemperatureUnit = "F", HotTemperature = 77, ThermalNotifications = true };
        window.PreparePreviewAsync(stress, "Settings").GetAwaiter().GetResult(); Pump();
        var hot = (ComboBox)window.FindName("HotThresholdCombo");
        if (hot.SelectedItem is not ComboBoxItem { Tag: 77 }) throw new InvalidOperationException("Opening settings rounded a migrated threshold.");
        if (((ComboBoxItem)hot.SelectedItem).Content.ToString() != "171 °F") throw new InvalidOperationException("Fahrenheit thresholds must round without integer truncation.");
        var critical = (ComboBox)window.FindName("CriticalThresholdCombo");
        critical.SelectedIndex = 0;
        if (((Button)window.FindName("SaveSettingsButton")).IsEnabled) throw new InvalidOperationException("Invalid thresholds can be saved.");
        ((RadioButton)window.FindName("CelsiusRadio")).IsChecked = true;
        if (hot.SelectedItem is not ComboBoxItem { Tag: 77 }) throw new InvalidOperationException("Changing temperature units changed the threshold.");
        foreach (var page in new[] { "Balanced", "Compact", "Detailed" })
        {
            stress.ViewMode = page;
            window.PreparePreviewAsync(stress, page).GetAwaiter().GetResult(); Pump();
            Save(window, Path.Combine(output, "320-fahrenheit-" + page + ".png"));
        }
        // Presentation order follows the setting, in every view that pairs the two components.
        window.Width = 1200; window.Height = 820; Pump();
        var order = new AppSettings { GpuFirst = true };
        foreach (var page in new[] { "Balanced", "Compact", "Detailed" })
        {
            order.ViewMode = page;
            window.PreparePreviewAsync(order, page).GetAwaiter().GetResult(); Pump();
            Save(window, Path.Combine(output, "gpu-first-" + page + ".png"));
        }
        AssertFirstCard(window, "GpuHeroCard");
        order.GpuFirst = false; order.ViewMode = "Balanced";
        window.PreparePreviewAsync(order, "Balanced").GetAwaiter().GetResult(); Pump();
        AssertFirstCard(window, "CpuHeroCard");

        window.Close(); app.Shutdown();
        Console.WriteLine($"Layout: {count} window/language/theme/page combinations rendered to {output}");
    }

    private static void AssertFirstCard(Window window, string expected)
    {
        foreach (var name in new[] { "HeroGrid", "CompactCardsGrid", "DetailedChartGrid", "DetailedSensorGrid" })
        {
            var grid = (UniformGrid)window.FindName(name);
            if (grid.Children.Count != 2) throw new InvalidOperationException($"{name} no longer holds a component pair.");
        }
        if (!ReferenceEquals(((UniformGrid)window.FindName("HeroGrid")).Children[0], window.FindName(expected)))
            throw new InvalidOperationException($"{expected} is not the first card.");
    }

    private static void AssertWithin(FrameworkElement element, FrameworkElement parent, string name)
    {
        if (element.ActualWidth > parent.ActualWidth + 1)
            throw new InvalidOperationException($"{name}: horizontal overflow ({element.ActualWidth} > {parent.ActualWidth}).");
    }

    private static void Save(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
