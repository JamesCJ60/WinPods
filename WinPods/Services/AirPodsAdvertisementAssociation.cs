using WinPods.Models;

namespace WinPods.Services;

public static class AirPodsAdvertisementAssociation
{
    public static AirPodsDevice? FindPairedDevice(
        IEnumerable<AirPodsDevice> pairedDevices,
        AirPodsAdvertisement advertisement)
    {
        var devices = pairedDevices as IReadOnlyCollection<AirPodsDevice> ?? pairedDevices.ToArray();
        var addressMatch = devices.FirstOrDefault(device =>
            string.Equals(device.Address, advertisement.Address, StringComparison.OrdinalIgnoreCase));
        if (addressMatch is not null) return addressMatch;
        if (advertisement.ProductId == 0) return null;

        AirPodsDevice? productMatch = null;
        foreach (var device in devices)
        {
            if (device.ProductId != advertisement.ProductId) continue;
            if (productMatch is not null) return null;
            productMatch = device;
        }
        return productMatch;
    }
}
