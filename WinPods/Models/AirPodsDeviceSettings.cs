namespace WinPods.Models;

public sealed record AirPodsDeviceSettings
{
    public bool AutoPause { get; init; } = true;
    public bool OneAirPodPlayback { get; init; }
    public bool AutoSwitch { get; init; }
    public bool? BluetoothTelephonyEnabled { get; init; }
    public int? LastCaseBattery { get; init; }
    public DateTimeOffset? LastCaseBatterySeen { get; init; }
    public byte? DeviceColour { get; init; }
    public ThreeBandEqualiserProfile Equaliser { get; init; } = ThreeBandEqualiserProfile.Recommended;
}
