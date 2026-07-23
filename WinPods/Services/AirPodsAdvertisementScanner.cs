using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using WinPods.Models;

namespace WinPods.Services;

public sealed class AirPodsAdvertisementScanner : IDisposable
{
    private const ushort AppleCompanyId = 0x004C;
    private readonly BluetoothLEAdvertisementWatcher _watcher;

    public AirPodsAdvertisementScanner()
    {
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _watcher.Received += OnReceived;
    }

    public event EventHandler<AirPodsAdvertisement>? AdvertisementReceived;

    public void Start()
    {
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) return;
        _watcher.Start();
    }

    public void Dispose()
    {
        _watcher.Received -= OnReceived;
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) _watcher.Stop();
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var section in args.Advertisement.ManufacturerData)
        {
            if (section.CompanyId != AppleCompanyId) continue;

            var bytes = ReadBuffer(section.Data);
            var address = FormatAddress(args.BluetoothAddress);
            if (AirPodsAdvertisementParser.TryParse(address, bytes, out var advertisement) && advertisement is not null)
            {
                AdvertisementReceived?.Invoke(this, advertisement);
            }
        }
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static string FormatAddress(ulong address)
    {
        var hex = address.ToString("X12");
        return string.Join(":", Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2)));
    }
}
