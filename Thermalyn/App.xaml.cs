using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Thermalyn.Services;

namespace Thermalyn;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thermalyn", "crash.log");

    private readonly bool _previewMode;

    public App() { }

    internal App(bool previewMode) => _previewMode = previewMode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (_previewMode) return;

        if (e.Args.Contains("--register-startup", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var service = new SettingsService();
                var settings = service.Load();
                settings.StartWithWindows = StartupService.EnsureListed(settings.StartWithWindows);
                service.Save(settings);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                Write(exception);
                Shutdown(1);
            }
            return;
        }

        if (!IsAdministrator())
        {
            try
            {
                if (!StartupService.TryLaunchRegisteredTask()) RelaunchAsAdministrator(e.Args);
            }
            catch (Exception exception) { Write(exception); }
            Shutdown(0);
            return;
        }

        LocalizationService.Apply(new SettingsService().Load().Language);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report(args.ExceptionObject as Exception, fatal: true);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Write(args.Exception);
        };
        new MainWindow().Show();
    }

    private static bool IsAdministrator()
    {
        using var current = Process.GetCurrentProcess();
        if (!OpenProcessToken(current.Handle, TokenQuery, out var token)) return false;
        using (token)
        {
            var elevation = new TokenElevation();
            var size = Marshal.SizeOf<TokenElevation>();
            return GetTokenInformation(token, TokenInformationClass.TokenElevation, ref elevation, size, out _)
                && elevation.TokenIsElevated != 0;
        }
    }

    private static void RelaunchAsAdministrator(IEnumerable<string> arguments)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The application path is unavailable.");
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--elevated");
        Process.Start(startInfo);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report(e.Exception, fatal: false);
    }

    private static void Report(Exception? exception, bool fatal)
    {
        Write(exception);
        var title = LocalizationService.Get("Error.Title");
        var body = LocalizationService.Format("Error.Body", exception?.Message ?? "unknown error", LogPath);
        MessageBox.Show(body, title, MessageBoxButton.OK, fatal ? MessageBoxImage.Error : MessageBoxImage.Warning);
    }

    private static void Write(Exception? exception)
    {
        if (exception is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var entry = new StringBuilder()
                .AppendLine($"--- {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ---")
                .AppendLine($"version {typeof(App).Assembly.GetName().Version}  os {Environment.OSVersion.VersionString}  x64 {Environment.Is64BitOperatingSystem}")
                .AppendLine(exception.ToString())
                .AppendLine();
            File.AppendAllText(LogPath, entry.ToString());
        }
        catch { }
    }

    private const uint TokenQuery = 0x0008;

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        TokenInformationClass tokenInformationClass,
        ref TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
