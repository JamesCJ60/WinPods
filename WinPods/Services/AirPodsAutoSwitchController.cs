using WinPods.Models;

namespace WinPods.Services;

public sealed class AirPodsAutoSwitchController(
    ISystemMediaTransport mediaTransport,
    IAirPodsConnectionService pairingService)
{
    private readonly Dictionary<string, int> _wornReadings = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextAttemptAt;

    public async Task<string?> ObserveAsync(
        AirPodsDevice device,
        AirPodsAdvertisement advertisement,
        AirPodsDeviceSettings settings)
    {
        if (!settings.AutoSwitch || device.IsConnected || device.IsCaseOpen ||
            advertisement.IsCaseOpen || advertisement.InEarCount is null)
        {
            _wornReadings.Remove(device.Id);
            return null;
        }

        if (advertisement.InEarCount.Value == 0)
        {
            _wornReadings.Remove(device.Id);
            return null;
        }

        var readings = _wornReadings.TryGetValue(device.Id, out var existing) ? existing + 1 : 1;
        _wornReadings[device.Id] = readings;
        if (readings < (device.IsOverEar ? 1 : 2)) return null;

        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextAttemptAt) return null;
            if (!await mediaTransport.HasActivePlaybackAsync()) return null;
            _nextAttemptAt = now.AddSeconds(30);
            var result = await pairingService.RequestConnectAsync(device);
            return result.Message;
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
