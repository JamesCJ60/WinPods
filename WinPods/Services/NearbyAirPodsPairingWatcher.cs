using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using WinPods.Models;

namespace WinPods.Services;

public sealed class NearbyAirPodsPairingWatcher : IDisposable
{
    private const string IsPresentProperty = "System.Devices.Aep.IsPresent";
    private const string ContainerIdProperty = "System.Devices.Aep.ContainerId";
    private readonly DeviceWatcher _watcher;
    private readonly ConcurrentDictionary<string, DeviceInformation> _devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _dismissedIds =
        new(StringComparer.OrdinalIgnoreCase);

    public NearbyAirPodsPairingWatcher()
    {
        _watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(false),
            [IsPresentProperty, ContainerIdProperty]);
        _watcher.Added += OnAdded;
        _watcher.Updated += OnUpdated;
        _watcher.Removed += OnRemoved;
    }

    public event EventHandler<AirPodsPairingCandidate>? PairingDeviceFound;
    public event EventHandler<string>? PairingDeviceLost;

    public void Start()
    {
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            return;
        _watcher.Start();
    }

    public void Dismiss(string id) => _dismissedIds.TryAdd(id, 0);

    public void Dispose()
    {
        _watcher.Added -= OnAdded;
        _watcher.Updated -= OnUpdated;
        _watcher.Removed -= OnRemoved;
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            _watcher.Stop();
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        _devices[info.Id] = info;
        PublishIfPairable(info);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (!_devices.TryGetValue(update.Id, out var info)) return;
        info.Update(update);
        PublishIfPairable(info);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        _devices.TryRemove(update.Id, out _);
        _dismissedIds.TryRemove(update.Id, out _);
        PairingDeviceLost?.Invoke(this, update.Id);
    }

    private void PublishIfPairable(DeviceInformation info)
    {
        if (_dismissedIds.ContainsKey(info.Id) ||
            info.Pairing.IsPaired ||
            IsKnownToBeAbsent(info) ||
            !AirPodsDiscoveryService.LooksLikeSupportedDevice(info.Name))
            return;

        PairingDeviceFound?.Invoke(
            this,
            new AirPodsPairingCandidate(
                info.Id,
                AirPodsRegistryReader.NormaliseDisplayName(info.Name),
                AirPodsProductCatalogue.InferProductId(info.Name),
                ContainerId: ReadContainerId(info)));
    }

    private static bool IsKnownToBeAbsent(DeviceInformation info) =>
        info.Properties.TryGetValue(IsPresentProperty, out var value) &&
        value is bool isPresent &&
        !isPresent;

    private static Guid? ReadContainerId(DeviceInformation info)
    {
        if (!info.Properties.TryGetValue(ContainerIdProperty, out var value) || value is null)
            return null;
        if (value is Guid identifier) return identifier;
        return Guid.TryParse(value.ToString(), out identifier) ? identifier : null;
    }
}
