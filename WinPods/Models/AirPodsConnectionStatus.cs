namespace WinPods.Models;

/// <summary>
/// How connected the AirPods are to this PC. Being paired and having a Bluetooth link
/// is not the same as Windows actually sending audio to them, so the states are kept apart.
/// </summary>
public enum AirPodsConnectionStatus
{
    /// <summary>Paired, but there is no Bluetooth link to this PC (e.g. in use on a phone).</summary>
    NotConnected,

    /// <summary>The Bluetooth link is up, but Windows has no active audio endpoint for the AirPods yet.</summary>
    AudioUnavailable,

    /// <summary>The AirPods audio endpoint is available, but Windows is playing through another output.</summary>
    Ready,

    /// <summary>Windows audio is going to the AirPods (default output, or the driverless EQ target).</summary>
    Active
}
