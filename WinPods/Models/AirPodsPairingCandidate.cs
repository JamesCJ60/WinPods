using System;

namespace WinPods.Models;

public sealed record AirPodsPairingCandidate(
    string Id,
    string Name,
    int ProductId = 0,
    byte? DeviceColour = null,
    Guid? ContainerId = null)
{
    public string Model => AirPodsProductCatalogue.GetModelName(ProductId, Name);

    public string ProductImagePath => AirPodsProductCatalogue.GetProductImagePath(ProductId, DeviceColour);

    public System.Windows.Rect ProductImageViewbox => AirPodsProductCatalogue.GetProductImageViewbox(ProductId);
}
