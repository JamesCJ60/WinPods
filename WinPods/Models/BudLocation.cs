namespace WinPods.Models;

/// <summary>Where an individual earbud is, as reported by the AirPods' own BLE advertisement.</summary>
public enum BudLocation
{
    Unknown,
    InEar,
    OutOfEar,
    InCase
}
