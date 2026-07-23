using Microsoft.Win32;
using System.IO;

namespace WinPods.Services;

public static class TrayIconPromotionService
{
    private const string SettingsPath = @"Control Panel\NotifyIconSettings";

    public static bool TryPromoteWinPods()
    {
        var promoted = false;
        try
        {
            var processPath = Path.GetFullPath(Environment.ProcessPath ?? string.Empty);
            using var settings = Registry.CurrentUser.OpenSubKey(SettingsPath, writable: false);
            if (settings is null) return false;

            foreach (var keyName in settings.GetSubKeyNames())
            {
                using var candidate = settings.OpenSubKey(keyName, writable: false);
                var executablePath = candidate?.GetValue("ExecutablePath") as string;
                var tooltip = candidate?.GetValue("InitialTooltip") as string;
                var matchesPath = PathsMatch(executablePath, processPath);
                var matchesTooltip = tooltip?.Contains("WinPods", StringComparison.OrdinalIgnoreCase) == true;
                if (!matchesPath && !matchesTooltip) continue;

                using var writable = settings.OpenSubKey(keyName, writable: true);
                if (writable is null) continue;
                writable.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                promoted = true;
            }
        }
        catch
        {
        }

        return promoted;
    }

    private static bool PathsMatch(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)), right,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
