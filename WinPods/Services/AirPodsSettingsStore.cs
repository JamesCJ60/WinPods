using System.IO;
using System.Text.Json;
using WinPods.Models;

namespace WinPods.Services;

public sealed class AirPodsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Dictionary<string, AirPodsDeviceSettings> _settings;

    public AirPodsSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinPods",
            "device-settings.json");
        _settings = LoadAll();
    }

    public AirPodsDeviceSettings Get(string address, ThreeBandEqualiserProfile? legacyEqualiser = null)
    {
        var key = NormaliseKey(address);
        if (_settings.TryGetValue(key, out var settings)) return Normalise(settings);
        return new AirPodsDeviceSettings { Equaliser = legacyEqualiser?.Normalised() ?? ThreeBandEqualiserProfile.Recommended };
    }

    public string Save(string address, AirPodsDeviceSettings settings)
    {
        try
        {
            _settings[NormaliseKey(address)] = Normalise(settings);
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, JsonOptions));
            return "Settings saved for these AirPods.";
        }
        catch (Exception exception)
        {
            return $"The AirPods settings could not be saved: {exception.Message}";
        }
    }

    private Dictionary<string, AirPodsDeviceSettings> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
            var values = JsonSerializer.Deserialize<Dictionary<string, AirPodsDeviceSettings>>(File.ReadAllText(_path));
            return values is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(values, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static AirPodsDeviceSettings Normalise(AirPodsDeviceSettings settings) => settings with
    {
        Equaliser = settings.Equaliser.Normalised()
    };

    private static string NormaliseKey(string address) =>
        address.Replace(":", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
}
