using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinPods.Services;

public sealed record AirPodsRegistryMetadata(int ProductId, string? StoredName);

public static partial class AirPodsRegistryReader
{
    public static AirPodsRegistryMetadata Read(string bluetoothAddress)
    {
        try
        {
            var keyName = bluetoothAddress.Replace(":", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices\{keyName}");
            if (key is null) return new(0, null);

            var productId = key.GetValue("PID") is object rawPid ? NormaliseProductId(Convert.ToInt32(rawPid)) : 0;
            var storedName = key.GetValue("Name") is byte[] nameBytes ? DecodeName(nameBytes) : null;
            return new(productId, storedName);
        }
        catch (Exception)
        {
            return new(0, null);
        }
    }

    public static string NormaliseDisplayName(string name)
    {
        var normalised = FindMySuffix().Replace(name.Trim(), string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalised) ? "AirPods" : normalised;
    }

    public static int NormaliseProductId(int windowsProductId) =>
        ((windowsProductId & 0xFF) << 8) | ((windowsProductId >> 8) & 0xFF);

    private static string? DecodeName(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        var name = Encoding.UTF8.GetString(bytes, 0, length).Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    [GeneratedRegex(@"\s*[-–—]\s*Find\s+My\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FindMySuffix();
}
