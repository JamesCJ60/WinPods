using Microsoft.Win32;

namespace WinPods.Services;

public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinPods";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public StartupSettingResult SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (enabled)
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                    return new(IsEnabled(), "WinPods could not determine its executable path.");
                key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
                return new(true, "WinPods will start when you sign in to Windows.");
            }

            key.DeleteValue(ValueName, false);
            return new(false, "WinPods will no longer start with Windows.");
        }
        catch (Exception exception)
        {
            return new(IsEnabled(), $"The Windows startup setting could not be changed: {exception.Message}");
        }
    }
}

public sealed record StartupSettingResult(bool IsEnabled, string Message);
