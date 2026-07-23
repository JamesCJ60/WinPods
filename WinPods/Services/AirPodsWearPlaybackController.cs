using WinPods.Models;

namespace WinPods.Services;

public interface ISystemMediaTransport
{
    Task<bool> PauseAsync();
    Task<bool> PlayAsync();
    Task<bool> HasActivePlaybackAsync();
}

public sealed class AirPodsWearPlaybackController(ISystemMediaTransport transport)
{
    private readonly Dictionary<string, bool> _wearStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _oneAirPodModes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingWearState> _pendingWearStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _pausedByDeviceId;

    public AirPodsWearPlaybackController() : this(new WindowsSystemMediaTransport())
    {
    }

    public async Task ObserveAsync(
        string deviceId,
        bool isConnected,
        bool isOverEar,
        int? inEarCount,
        AirPodsDeviceSettings settings)
    {
        await _gate.WaitAsync();
        try
        {
            if (!isConnected || !settings.AutoPause)
            {
                ClearDevice(deviceId);
                return;
            }

            if (inEarCount is null) return;
            var oneAirPodMode = settings.OneAirPodPlayback && !isOverEar;
            var requiredReadings = 1;
            var isBeingWorn = oneAirPodMode
                ? inEarCount.Value > 0
                : inEarCount.Value >= (isOverEar ? 1 : 2);

            if (_oneAirPodModes.TryGetValue(deviceId, out var previousMode) && previousMode != oneAirPodMode)
            {
                ResetWearState(deviceId);
            }
            _oneAirPodModes[deviceId] = oneAirPodMode;

            if (!_wearStates.TryGetValue(deviceId, out var wasBeingWorn))
            {
                if (!isBeingWorn)
                {
                    var initialPending = _pendingWearStates.TryGetValue(deviceId, out var initialExisting) &&
                                         !initialExisting.IsBeingWorn
                        ? initialExisting with { Readings = initialExisting.Readings + 1 }
                        : new PendingWearState(false, 1);
                    _pendingWearStates[deviceId] = initialPending;
                    if (initialPending.Readings < requiredReadings) return;

                    _pendingWearStates.Remove(deviceId);
                    _wearStates[deviceId] = false;
                    if (await transport.PauseAsync())
                        _pausedByDeviceId = deviceId;
                    return;
                }

                _wearStates[deviceId] = isBeingWorn;
                if (isBeingWorn &&
                    string.Equals(_pausedByDeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
                    await transport.PlayAsync())
                {
                    _pausedByDeviceId = null;
                    return;
                }
                return;
            }

            if (wasBeingWorn == isBeingWorn)
            {
                _pendingWearStates.Remove(deviceId);
                if (isBeingWorn &&
                    string.Equals(_pausedByDeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
                    await transport.PlayAsync())
                {
                    _pausedByDeviceId = null;
                }
                return;
            }

            var pending = _pendingWearStates.TryGetValue(deviceId, out var existing) &&
                          existing.IsBeingWorn == isBeingWorn
                ? existing with { Readings = existing.Readings + 1 }
                : new PendingWearState(isBeingWorn, 1);
            _pendingWearStates[deviceId] = pending;
            if (pending.Readings < requiredReadings) return;

            _pendingWearStates.Remove(deviceId);
            _wearStates[deviceId] = isBeingWorn;
            if (wasBeingWorn && !isBeingWorn)
            {
                if (await transport.PauseAsync())
                    _pausedByDeviceId = deviceId;
                return;
            }

            if (isBeingWorn &&
                string.Equals(_pausedByDeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
                await transport.PlayAsync())
            {
                _pausedByDeviceId = null;
            }
        }
        catch
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ClearDevice(string deviceId)
    {
        ResetWearState(deviceId);
        if (string.Equals(_pausedByDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            _pausedByDeviceId = null;
    }

    private void ResetWearState(string deviceId)
    {
        _wearStates.Remove(deviceId);
        _oneAirPodModes.Remove(deviceId);
        _pendingWearStates.Remove(deviceId);
    }

    private sealed record PendingWearState(bool IsBeingWorn, int Readings);
}
