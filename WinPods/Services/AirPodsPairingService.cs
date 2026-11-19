using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using WinPods.Models;

namespace WinPods.Services;

public sealed class AirPodsPairingService
{
    // AirPods coming over from a phone or out of a sleeping case can take several seconds.
    private const int ConnectPollAttempts = 24;
    private static readonly TimeSpan ConnectPollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<AirPodsConnectionAttempt> PairNearbyAsync(AirPodsPairingCandidate candidate)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(candidate.Id);
            if (info is null)
                return new(false, "These AirPods are no longer available for pairing.");
            if (info.Pairing.IsPaired)
                return new(true, $"{candidate.Name} are already paired.");
            var result = await info.Pairing.PairAsync(DevicePairingProtectionLevel.Default);
            return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired
                ? new(true, $"{candidate.Name} paired successfully.")
                : new(false, $"Windows could not pair these AirPods ({result.Status}).");
        }
        catch (Exception exception)
        {
            return new(false, $"Windows could not pair these AirPods: {exception.Message}");
        }
    }

    public async Task<AirPodsConnectionAttempt> TryConnectAsync(AirPodsDevice device)
    {
        try
        {
            if (device.IsConnected && TrySelectAudioEndpoint(device))
                return new(true, $"Switched Windows audio to {device.Name}.");

            var request = await RequestConnectAsync(device);
            if (!request.Success) return request;

            for (var attempt = 0; attempt < ConnectPollAttempts; attempt++)
            {
                using var bluetoothDevice = await BluetoothDevice.FromIdAsync(device.Id);
                if (bluetoothDevice?.ConnectionStatus == BluetoothConnectionStatus.Connected &&
                    TrySelectAudioEndpoint(device))
                    return new(true, $"Switched Windows audio to {device.Name}.");

                await Task.Delay(ConnectPollInterval);
            }

            if (TrySelectAudioEndpoint(device))
                return new(true, $"Switched Windows audio to {device.Name}.");

            return new(false,
                $"{device.Name} didn't connect. They may be busy on another device or asleep in a closed case; open the case and try again.");
        }
        catch (Exception exception)
        {
            return new(false, $"Windows could not connect these AirPods: {exception.Message}");
        }
    }

    public async Task<AirPodsConnectionAttempt> RequestConnectAsync(AirPodsDevice device)
    {
        try
        {
            // A Bluetooth link on its own doesn't mean Windows can play audio to the AirPods,
            // so only skip the audio reconnect request once their audio endpoint is up.
            if (device.ConnectionStatus is AirPodsConnectionStatus.Ready or AirPodsConnectionStatus.Active)
                return new(true, $"{device.Name} is already connected.");

            var info = await DeviceInformation.CreateFromIdAsync(device.Id);
            if (info is null) return new(false, "Windows could not find these paired AirPods.");

            var pairing = info.Pairing;
            if (!pairing.IsPaired && !pairing.CanPair)
                return new(false, "Windows cannot pair these AirPods until their case is in pairing mode.");

            if (!pairing.IsPaired)
            {
                var result = await pairing.PairAsync(DevicePairingProtectionLevel.Default);
                if (result.Status is not (DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired))
                    return new(false, $"Windows rejected the Bluetooth connection request ({result.Status}).");
            }

            return await Task.Run(() => WindowsBluetoothAudioConnector.RequestConnect(device));
        }
        catch (Exception exception)
        {
            return new(false, $"Windows could not request the Bluetooth connection: {exception.Message}");
        }
    }

    private static bool TrySelectAudioEndpoint(AirPodsDevice device)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            var endpoint = endpoints
                .OrderByDescending(candidate => EndpointScore(candidate.FriendlyName, device))
                .FirstOrDefault(candidate => EndpointScore(candidate.FriendlyName, device) > 20);
            if (endpoint is null) return false;

            WindowsDefaultAudioEndpoint.Set(
                endpoint.ID,
                WindowsDefaultAudioEndpoint.EndpointRole.Console,
                WindowsDefaultAudioEndpoint.EndpointRole.Multimedia,
                WindowsDefaultAudioEndpoint.EndpointRole.Communications);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int EndpointScore(string endpointName, AirPodsDevice device)
    {
        var endpoint = Normalise(endpointName);
        var name = Normalise(device.Name);
        var model = Normalise(device.Model);
        var score = endpoint.Contains(name, StringComparison.Ordinal) ? 200 : 0;
        if (endpoint.Contains(model, StringComparison.Ordinal)) score += 100;
        if (endpoint.Contains("airpods", StringComparison.Ordinal)) score += 20;
        if (endpoint.Contains("handsfree", StringComparison.Ordinal)) score -= 250;
        return score;
    }

    private static string Normalise(string value) => new(value
        .ToLowerInvariant()
        .Where(character => char.IsLetterOrDigit(character))
        .ToArray());
}

public sealed record AirPodsConnectionAttempt(bool Success, string Message);
