using NAudio.CoreAudioApi;
using WinPods.Models;

namespace WinPods.Services;

public sealed class AirPodsAudioConnectionState
{
    private readonly Dictionary<string, CachedState> _states = new(StringComparer.OrdinalIgnoreCase);

    public bool IsActive(AirPodsDevice device)
    {
        var now = DateTimeOffset.UtcNow;
        if (_states.TryGetValue(device.Id, out var cached) && now - cached.CheckedAt < TimeSpan.FromSeconds(2))
            return cached.IsActive;

        var isActive = FindActiveEndpoint(device);
        _states[device.Id] = new(isActive, now);
        return isActive;
    }

    private static bool FindActiveEndpoint(AirPodsDevice device)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var name = Normalise(device.Name);
            var model = Normalise(device.Model);
            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Any(endpoint =>
                {
                    var endpointName = Normalise(endpoint.FriendlyName);
                    return endpointName.Contains(name, StringComparison.Ordinal) ||
                           endpointName.Contains(model, StringComparison.Ordinal);
                });
        }
        catch
        {
            return false;
        }
    }

    private static string Normalise(string value) => new(value
        .ToLowerInvariant()
        .Where(character => char.IsLetterOrDigit(character))
        .ToArray());

    private sealed record CachedState(bool IsActive, DateTimeOffset CheckedAt);
}
