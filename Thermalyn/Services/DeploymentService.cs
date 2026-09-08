using System.IO;
using Microsoft.Win32;

namespace Thermalyn.Services;

public static class DeploymentService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F2C1D74-4E63-4A18-9E2B-5C7A0D3F1B96}_is1";

    public static bool IsInstalled()
    {
        var current = Path.GetDirectoryName(Environment.ProcessPath ?? "");
        if (string.IsNullOrEmpty(current)) return false;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is not string { Length: > 0 } location) continue;
                if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                                  Path.TrimEndingDirectorySeparator(Path.GetFullPath(current)),
                                  StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
        }
        return false;
    }
}
