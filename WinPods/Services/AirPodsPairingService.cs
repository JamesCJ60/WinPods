using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.System;
using WinPods.Models;

namespace WinPods.Services;

public interface IAirPodsConnectionService
{
    Task<AirPodsConnectionAttempt> RequestConnectAsync(AirPodsDevice device);
}

public sealed class AirPodsPairingService : IAirPodsConnectionService
{
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

    public async Task<string> PairOrSwitchAsync(AirPodsDevice device)
    {
        var attempt = await TryConnectAsync(device);
        if (attempt.Success) return attempt.Message;

        try
        {
            var opened = await Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));
            return opened
                ? $"{attempt.Message} Bluetooth settings opened so you can connect them manually."
                : attempt.Message;
        }
        catch
        {
            return attempt.Message;
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

            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var bluetoothDevice = await BluetoothDevice.FromIdAsync(device.Id);
                if (bluetoothDevice?.ConnectionStatus == BluetoothConnectionStatus.Connected &&
                    TrySelectAudioEndpoint(device))
                    return new(true, $"Switched Windows audio to {device.Name}.");

                await Task.Delay(500);
            }

            if (TrySelectAudioEndpoint(device))
                return new(true, $"Switched Windows audio to {device.Name}.");

            return new(false, "Windows accepted the connection request but the AirPods audio endpoint did not become available.");
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
            if (device.IsConnected)
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
