using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Thermalyn.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string LegacyStartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ValueName = "Thermalyn";
    private const string TaskName = "Thermalyn";

    public static void SetEnabled(bool enabled)
    {
        EnsureRegistration();
        SetStartupApproval(enabled);
    }

    public static bool EnsureListed(bool preferredEnabled)
    {
        EnsureRegistration();
        var currentState = ReadStartupApproval();
        if (currentState.HasValue) return currentState.Value;

        SetStartupApproval(preferredEnabled);
        return preferredEnabled;
    }

    public static void RemoveRegistration()
    {
        DeleteRunEntry();
        DeleteStartupShortcut();
        ClearStartupApproval();
        ClearLegacyStartupApproval();
        RunSchtasks(["/Delete", "/TN", TaskName, "/F"], tolerateFailure: true);
    }

    private static void EnsureRegistration()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The application path is unavailable.");
        RegisterElevatedTask(executable);

        // Startup Apps reads the Run entry. It launches unelevated and hands off to the task.
        // That is what skips the UAC prompt.
        SetRunEntry(executable);
        DeleteStartupShortcut();
        ClearLegacyStartupApproval();
    }

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{ValueName}.lnk");

    private static void SetRunEntry(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
    }

    public static bool TryLaunchRegisteredTask()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return false;

        var scheduler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        var query = new ProcessStartInfo(scheduler)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/Query", "/TN", TaskName, "/XML" }) query.ArgumentList.Add(argument);
        using (var process = Process.Start(query))
        {
            if (process is null) return false;
            var xml = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) return false;
            try
            {
                var document = XDocument.Parse(xml);
                var command = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Command")?.Value;
                if (!string.Equals(Path.GetFullPath(command ?? ""), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        var run = new ProcessStartInfo(scheduler) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/Run", "/TN", TaskName }) run.ArgumentList.Add(argument);
        using var scheduledRun = Process.Start(run);
        if (scheduledRun is null) return false;
        scheduledRun.WaitForExit();
        return scheduledRun.ExitCode == 0;
    }

    private static void DeleteStartupShortcut()
    {
        try { File.Delete(ShortcutPath); } catch { }
    }

    private static void DeleteRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, false);
    }

    private static void ClearStartupApproval()
    {
        using var approvalKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
        approvalKey?.DeleteValue(ValueName, false);
    }

    private static void ClearLegacyStartupApproval()
    {
        using var approvalKey = Registry.CurrentUser.OpenSubKey(LegacyStartupApprovedKey, writable: true);
        approvalKey?.DeleteValue($"{ValueName}.lnk", false);
    }

    private static bool? ReadStartupApproval()
    {
        using var approvalKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
        if (approvalKey?.GetValue(ValueName) is not byte[] { Length: > 0 } data) return null;
        return data[0] switch
        {
            2 => true,
            3 => false,
            _ => null
        };
    }

    private static void SetStartupApproval(bool enabled)
    {
        using var approvalKey = Registry.CurrentUser.CreateSubKey(StartupApprovedKey, writable: true)
            ?? throw new InvalidOperationException("Windows startup approval settings are unavailable.");
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        approvalKey.SetValue(ValueName, data, RegistryValueKind.Binary);
    }

    private static void RegisterElevatedTask(string executable)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user could not be identified.");
        var workingDirectory = Path.GetDirectoryName(executable) ?? "";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Thermalyn startup</Description></RegistrationInfo>
              <Triggers />
              <Principals>
                <Principal id="ThermalynUser">
                  <UserId>{SecurityElement.Escape(sid)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="ThermalynUser">
                <Exec>
                  <Command>{SecurityElement.Escape(executable)}</Command>
                  <Arguments>--startup</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(workingDirectory)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        var taskFile = Path.Combine(Path.GetTempPath(), $"Thermalyn-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(taskFile, xml, Encoding.Unicode);
            RunSchtasks(["/Create", "/TN", TaskName, "/XML", taskFile, "/F"], tolerateFailure: false);
        }
        finally
        {
            try { File.Delete(taskFile); } catch { }
        }
    }

    private static void RunSchtasks(string[] arguments, bool tolerateFailure)
    {
        var scheduler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        var startInfo = new ProcessStartInfo(scheduler) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Task Scheduler could not be started.");
        process.WaitForExit();
        if (!tolerateFailure && process.ExitCode != 0)
            throw new InvalidOperationException($"Task Scheduler returned code {process.ExitCode}.");
    }
}
