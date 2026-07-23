using System.IO;
using System.Text.Json;
using WinPods.Models;

namespace WinPods.Services;

public sealed class EqualiserProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public EqualiserProfileStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinPods");
        _path = Path.Combine(directory, "equalizer.json");
    }

    public ThreeBandEqualiserProfile Load()
    {
        try
        {
            if (!File.Exists(_path)) return ThreeBandEqualiserProfile.Recommended;
            return (JsonSerializer.Deserialize<ThreeBandEqualiserProfile>(File.ReadAllText(_path))
                    ?? ThreeBandEqualiserProfile.Recommended).Normalised();
        }
        catch (Exception)
        {
            return ThreeBandEqualiserProfile.Recommended;
        }
    }

    public string Save(ThreeBandEqualiserProfile profile)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(profile.Normalised(), JsonOptions));
            return "Curve saved locally.";
        }
        catch (Exception exception)
        {
            return $"The EQ profile could not be saved: {exception.Message}";
        }
    }
}
