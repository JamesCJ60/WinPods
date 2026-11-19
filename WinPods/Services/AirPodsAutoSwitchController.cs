using WinPods.Models;

namespace WinPods.Services;

/// <summary>
/// Decides when to <em>offer</em> switching the AirPods to this PC (worn, not connected here, and
/// the PC is playing media). It never connects by itself: grabbing the AirPods without asking
/// would fight the iPhone/Mac that is currently using them.
/// </summary>
public sealed class AirPodsAutoSwitchController(ISystemMediaTransport mediaTransport)
{
    // How long to wait before offering again after an offer was shown, or after "Not now".
    private static readonly TimeSpan OfferCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DeclineCooldown = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, int> _wornReadings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _nextOfferAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<bool> ShouldOfferSwitchAsync(
        AirPodsDevice device,
        AirPodsAdvertisement advertisement,
        AirPodsDeviceSettings settings)
    {
        if (!settings.AutoSwitch ||
            device.IsConnected ||
            device.ConnectionStatus != AirPodsConnectionStatus.NotConnected ||
            device.IsCaseOpen ||
            advertisement.IsCaseOpen ||
            advertisement.InEarCount is null or 0)
        {
            _wornReadings.Remove(device.Id);
            return false;
        }

        var readings = _wornReadings.TryGetValue(device.Id, out var existing) ? existing + 1 : 1;
        _wornReadings[device.Id] = readings;
        if (readings < (device.IsOverEar ? 1 : 2)) return false;

        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_nextOfferAt.TryGetValue(device.Id, out var nextOffer) && now < nextOffer) return false;
            if (!await mediaTransport.HasActivePlaybackAsync()) return false;
            _nextOfferAt[device.Id] = now + OfferCooldown;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The user said "Not now": stay quiet about these AirPods for a while.</summary>
    public void Decline(AirPodsDevice device) =>
        _nextOfferAt[device.Id] = DateTimeOffset.UtcNow + DeclineCooldown;
}
