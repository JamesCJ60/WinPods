using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using WinPods.Models;

namespace WinPods.Services;

public sealed class AirPodsDiscoveryService
{
    private const string BatteryLifeProperty = "System.Devices.BatteryLife";
    private const string ContainerIdProperty = "System.Devices.ContainerId";
    private const string AssociationContainerIdProperty = "System.Devices.Aep.ContainerId";

    public async Task<IReadOnlyList<AirPodsDevice>> FindPairedAsync()
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var found = await DeviceInformation.FindAllAsync(
            selector,
            [BatteryLifeProperty, ContainerIdProperty, AssociationContainerIdProperty]);
        var devices = new List<AirPodsDevice>();

        foreach (var info in found.Where(info => LooksLikeSupportedDevice(info.Name)))
        {
            using var bluetoothDevice = await BluetoothDevice.FromIdAsync(info.Id);
            if (bluetoothDevice is null) continue;

            var address = FormatAddress(bluetoothDevice.BluetoothAddress);
            var metadata = AirPodsRegistryReader.Read(address);
            var systemName = metadata.StoredName ?? (string.IsNullOrWhiteSpace(bluetoothDevice.Name) ? info.Name : bluetoothDevice.Name);
            var name = AirPodsRegistryReader.NormaliseDisplayName(systemName);
            var device = new AirPodsDevice
            {
                Id = info.Id,
                Address = address,
                ProductId = metadata.ProductId,
                ContainerId = ReadContainerId(info),
                Name = name,
                Model = AirPodsProductCatalogue.GetModelName(metadata.ProductId, name),
                IsConnected = bluetoothDevice.ConnectionStatus == BluetoothConnectionStatus.Connected
            };

            if (device.IsOverEar && TryReadBattery(info, out var battery)) device.LeftBattery = battery;
            devices.Add(device);
        }

        return devices.OrderByDescending(device => device.IsConnected).ThenBy(device => device.Name).ToArray();
    }

    public static bool LooksLikeSupportedDevice(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Air Pods", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Beats", StringComparison.OrdinalIgnoreCase));

    private static bool TryReadBattery(DeviceInformation info, out int battery)
    {
        battery = 0;
        if (!info.Properties.TryGetValue(BatteryLifeProperty, out var raw) || raw is null) return false;
        try
        {
            battery = Convert.ToInt32(raw);
            return battery is >= 0 and <= 100;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Guid? ReadContainerId(DeviceInformation info)
    {
        foreach (var key in new[] { AssociationContainerIdProperty, ContainerIdProperty })
        {
            if (!info.Properties.TryGetValue(key, out var value) || value is null) continue;
            if (value is Guid identifier) return identifier;
            if (Guid.TryParse(value.ToString(), out identifier)) return identifier;
        }
        return null;
    }

    private static string FormatAddress(ulong address)
    {
        var hex = address.ToString("X12");
        return string.Join(":", Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2)));
    }
}
