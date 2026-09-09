using System.Text;
using System.Windows;
using Thermalyn.Models;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    private readonly SessionStatistics _session = new();

    private void TrackAndRenderSession()
    {
        _session.Add(_snapshot);
        MiniCpuValue.Text = Temperature(_snapshot.Cpu.Temperature);
        MiniCpuValue.Foreground = TemperatureBrush(_snapshot.Cpu.Temperature);
        MiniCpuSecondary.Text = LocalizationService.Format("Session.Load", Percent(_snapshot.Cpu.Load));
        MiniCpuRange.Text = SessionRange(_session.CpuTemperature.Summary, Temperature);

        MiniGpuValue.Text = Temperature(_snapshot.Gpu.Temperature);
        MiniGpuValue.Foreground = TemperatureBrush(_snapshot.Gpu.Temperature, ThermalComponent.Gpu);
        MiniGpuSecondary.Text = LocalizationService.Format("Session.Load", Percent(_snapshot.Gpu.Load));
        MiniGpuRange.Text = SessionRange(_session.GpuTemperature.Summary, Temperature);

        MiniRamValue.Text = Percent(_snapshot.Memory.Load);
        MiniRamSecondary.Text = _snapshot.Memory.Name;
        MiniRamRange.Text = SessionRange(_session.MemoryLoad.Summary, Percent);
        MiniSessionSince.Text = LocalizationService.Format("Session.Since", _session.StartedAt.LocalDateTime);
    }

    private static string SessionRange(SessionSummary? summary, Func<double?, string> format) => summary is { } value
        ? LocalizationService.Format("Session.Range", format(value.Minimum), format(value.Average), format(value.Maximum))
        : LocalizationService.Get("Session.NoData");

    private void ResetSession_Click(object sender, RoutedEventArgs e)
    {
        _session.Reset();
        TrackAndRenderSession();
    }

    private async void CopyDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        var text = BuildDiagnosticText();
        try
        {
            Clipboard.SetText(text);
            CopyDiagnosticButton.ToolTip = LocalizationService.Get("Session.Copied");
            await Task.Delay(1500);
            if (!_isClosed) CopyDiagnosticButton.ToolTip = LocalizationService.Get("Session.Copy");
        }
        catch (Exception exception)
        {
            SetStatus(LocalizationService.Format("Session.CopyFailed", exception.Message));
        }
    }

    internal string BuildDiagnosticText()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "—";
        var builder = new StringBuilder()
            .AppendLine($"Thermalyn {version}")
            .AppendLine($"{LocalizationService.Get("Session.Started")}: {_session.StartedAt.LocalDateTime:G}")
            .AppendLine($"{LocalizationService.Get("Common.Processor")}: {Temperature(_snapshot.Cpu.Temperature)} · {Percent(_snapshot.Cpu.Load)}")
            .AppendLine(SessionRange(_session.CpuTemperature.Summary, Temperature))
            .AppendLine($"{LocalizationService.Get("Common.GraphicsCard")}: {Temperature(_snapshot.Gpu.Temperature)} · {Percent(_snapshot.Gpu.Load)}")
            .AppendLine(SessionRange(_session.GpuTemperature.Summary, Temperature))
            .AppendLine($"{LocalizationService.Get("Common.Memory")}: {Percent(_snapshot.Memory.Load)}")
            .AppendLine(SessionRange(_session.MemoryLoad.Summary, Percent));
        if (!string.IsNullOrWhiteSpace(_snapshot.Error))
            builder.AppendLine($"{LocalizationService.Get("Common.Error")}: {_snapshot.Error}");
        return builder.ToString().TrimEnd();
    }
}
