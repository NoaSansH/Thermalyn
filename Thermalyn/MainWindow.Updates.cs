using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using Thermalyn.Services;

namespace Thermalyn;

public partial class MainWindow
{
    private readonly UpdateService _updateService = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private UpdateInfo? _availableUpdate;
    private VerifiedUpdateDownload? _downloadedUpdate;
    private bool _checkingForUpdate;
    private bool _downloadingUpdate;
    private bool _waitingForNetwork;
    private bool _hasCompletedUpdateCheck;
    private string? _updateStatusError;
    private DateTimeOffset _lastUpdateAttempt = DateTimeOffset.MinValue;

    private void StartUpdateMonitoring()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _ = CheckForUpdatesAsync();
    }

    private async void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable || !_waitingForNetwork || _isClosed) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), _updateCancellation.Token);
            if (_isClosed || !NetworkInterface.GetIsNetworkAvailable() || !_waitingForNetwork) return;
            var sinceLastAttempt = DateTimeOffset.UtcNow - _lastUpdateAttempt;
            if (sinceLastAttempt < TimeSpan.FromMinutes(1))
                await Task.Delay(TimeSpan.FromMinutes(1) - sinceLastAttempt, _updateCancellation.Token);
            await Dispatcher.InvokeAsync(() => _ = CheckForUpdatesAsync());
        }
        catch (OperationCanceledException) { }
    }

    private async Task CheckForUpdatesAsync(bool openPopup = false)
    {
        if (_previewMode || _checkingForUpdate || _downloadingUpdate) return;
        _checkingForUpdate = true;
        _updateStatusError = null;
        SettingsCheckUpdateButton.IsEnabled = false;
        SettingsUpdateStatusText.Text = LocalizationService.Get("Update.Checking");
        UpdateErrorText.Visibility = Visibility.Collapsed;
        UpdateRetryButton.Visibility = Visibility.Collapsed;
        UpdateCheckingText.Visibility = Visibility.Visible;
        UpdateButton.ToolTip = LocalizationService.Get("Update.Checking");
        if (openPopup) UpdatePopup.IsOpen = true;

        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                _waitingForNetwork = true;
                ShowUpdateError(LocalizationService.Format("Update.CheckFailed", LocalizationService.Get("Update.NetworkError")));
                return;
            }
            _lastUpdateAttempt = DateTimeOffset.UtcNow;
            var installed = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
            _availableUpdate = await _updateService.CheckAsync(installed, _updateCancellation.Token);
            _waitingForNetwork = false;
            _hasCompletedUpdateCheck = true;
            if (_isClosed) return;
            UpdateCurrentVersionText.Text = installed.ToString(3);
            if (_availableUpdate is null)
            {
                UpdateBadge.Visibility = Visibility.Collapsed;
                UpdateAvailablePanel.Visibility = Visibility.Collapsed;
                UpdateUpToDateText.Visibility = Visibility.Visible;
                UpdateButton.ToolTip = LocalizationService.Get("Update.UpToDate");
                SettingsUpdateStatusText.Text = LocalizationService.Get("Update.UpToDate");
            }
            else
            {
                UpdateLatestVersionText.Text = _availableUpdate.Version.ToString(3);
                UpdateReleaseNotesText.Text = string.IsNullOrWhiteSpace(_availableUpdate.ReleaseNotes)
                    ? LocalizationService.Get("Update.NoNotes")
                    : _availableUpdate.ReleaseNotes.Trim();
                UpdateBadge.Visibility = Visibility.Visible;
                UpdateAvailablePanel.Visibility = Visibility.Visible;
                UpdateUpToDateText.Visibility = Visibility.Collapsed;
                UpdateDownloadButton.Visibility = Visibility.Visible;
                UpdateButton.ToolTip = LocalizationService.Format("Update.AvailableTooltip", _availableUpdate.Version.ToString(3));
                SettingsUpdateStatusText.Text = LocalizationService.Format("Update.SettingsAvailable", _availableUpdate.Version.ToString(3));
            }
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (exception is HttpRequestException or TaskCanceledException) _waitingForNetwork = true;
            ShowUpdateError(LocalizationService.Format("Update.CheckFailed", FriendlyUpdateError(exception)));
        }
        finally
        {
            _checkingForUpdate = false;
            SettingsCheckUpdateButton.IsEnabled = true;
            UpdateCheckingText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdatePopup.IsOpen = !UpdatePopup.IsOpen;
        if (UpdatePopup.IsOpen && _availableUpdate is null && !_checkingForUpdate && UpdateErrorText.Visibility != Visibility.Visible)
            _ = CheckForUpdatesAsync(true);
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null || _downloadingUpdate) return;
        _downloadingUpdate = true;
        UpdateDownloadButton.Visibility = Visibility.Collapsed;
        UpdateRetryButton.Visibility = Visibility.Collapsed;
        UpdateErrorText.Visibility = Visibility.Collapsed;
        UpdateProgressPanel.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateProgressText.Text = LocalizationService.Get("Update.StartingDownload");

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                UpdateProgressBar.Value = value.Percentage;
                UpdateProgressText.Text = LocalizationService.Format("Update.DownloadProgress", value.Percentage);
            });
            _downloadedUpdate = await _updateService.DownloadAsync(_availableUpdate, progress, _updateCancellation.Token);
            if (_isClosed) return;
            UpdateProgressBar.Value = 100;
            UpdateProgressText.Text = LocalizationService.Get("Update.Verified");
            UpdateRestartButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            UpdateDownloadButton.Visibility = Visibility.Visible;
            ShowUpdateError(LocalizationService.Format("Update.DownloadFailed", FriendlyUpdateError(exception)));
        }
        finally { _downloadingUpdate = false; }
    }

    private async void RestartAndInstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadedUpdate is not { } download || !File.Exists(download.Path))
        {
            ShowUpdateError(LocalizationService.Get("Update.MissingDownload"));
            return;
        }

        try
        {
            UpdateRestartButton.IsEnabled = false;
            await using var verifiedInstaller = await UpdateService.OpenVerifiedInstallerAsync(download, _updateCancellation.Token);
            Process.Start(new ProcessStartInfo(download.Path)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(download.Path)!,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1"
            });
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            UpdateRestartButton.IsEnabled = true;
            ShowUpdateError(LocalizationService.Format("Update.InstallFailed", FriendlyUpdateError(exception)));
        }
    }

    private async void RetryUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);
    private async void SettingsCheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private void ShowUpdateError(string message)
    {
        _updateStatusError = message;
        UpdateErrorText.Text = message;
        UpdateErrorText.Visibility = Visibility.Visible;
        UpdateRetryButton.Visibility = Visibility.Visible;
        UpdateButton.ToolTip = LocalizationService.Get("Update.ErrorTooltip");
        SettingsUpdateStatusText.Text = message;
    }

    private void RefreshSettingsUpdateStatus()
    {
        SettingsUpdateStatusText.Text = _checkingForUpdate
            ? LocalizationService.Get("Update.Checking")
            : _availableUpdate is not null
                ? LocalizationService.Format("Update.SettingsAvailable", _availableUpdate.Version.ToString(3))
                : _updateStatusError ?? (_hasCompletedUpdateCheck
                    ? LocalizationService.Get("Update.UpToDate")
                    : LocalizationService.Format("Update.NotChecked", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "—"));
    }

    private static string FriendlyUpdateError(Exception exception) => exception switch
    {
        HttpRequestException => LocalizationService.Get("Update.NetworkError"),
        TaskCanceledException => LocalizationService.Get("Update.TimeoutError"),
        InvalidDataException => exception.Message,
        _ => exception.Message
    };
}
