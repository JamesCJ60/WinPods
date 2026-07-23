using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WinPods.Models;

namespace WinPods.Services;

public sealed record BluetoothTelephonyState(bool IsAvailable, bool IsEnabled, string Message);

public sealed record BluetoothTelephonyUpdate(bool Success, bool Changed, bool IsEnabled, string Message);

public interface IBluetoothTelephonyPlatform
{
    BluetoothTelephonyState Read(AirPodsDevice device);
    BluetoothTelephonyUpdate Set(AirPodsDevice device, bool enabled);
    Task<AirPodsConnectionAttempt> RestartConnectionAsync(AirPodsDevice device);
}

public sealed class BluetoothTelephonyService(
    IBluetoothTelephonyPlatform? platform = null)
{
    private readonly IBluetoothTelephonyPlatform _platform =
        platform ?? new WindowsBluetoothTelephonyPlatform();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public BluetoothTelephonyState Read(AirPodsDevice device) => _platform.Read(device);

    public async Task<BluetoothTelephonyUpdate> EnsureAsync(
        AirPodsDevice device,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var deviceLock = _deviceLocks.GetOrAdd(device.Address, _ => new SemaphoreSlim(1, 1));
        await deviceLock.WaitAsync(cancellationToken);
        try
        {
            var current = _platform.Read(device);
            if (!current.IsAvailable)
                return new(false, false, current.IsEnabled, current.Message);
            if (current.IsEnabled == enabled)
                return new(true, false, enabled, $"Bluetooth telephony is already {(enabled ? "enabled" : "disabled")}.");

            var update = _platform.Set(device, enabled);
            if (!update.Success)
            {
                var afterFailedWrite = _platform.Read(device);
                if (afterFailedWrite.IsAvailable && afterFailedWrite.IsEnabled == enabled)
                    return new(true, false, enabled,
                        $"Bluetooth telephony is already {(enabled ? "enabled" : "disabled")}.");
                return update with { IsEnabled = afterFailedWrite.IsEnabled };
            }

            var verified = _platform.Read(device);
            if (!verified.IsAvailable || verified.IsEnabled != enabled)
                return new(false, true, verified.IsEnabled, "Windows did not retain the Bluetooth telephony change.");

            if (!device.IsConnected)
                return new(true, true, enabled,
                    $"Bluetooth telephony {(enabled ? "enabled" : "disabled")}; it will apply on the next connection.");

            var reconnect = await _platform.RestartConnectionAsync(device);
            return reconnect.Success
                ? new(true, true, enabled,
                    $"Bluetooth telephony {(enabled ? "enabled" : "disabled")}; {device.Name} were reconnected.")
                : new(false, true, enabled,
                    $"Bluetooth telephony changed, but the reconnect failed: {reconnect.Message}");
        }
        finally
        {
            deviceLock.Release();
        }
    }
}

public sealed class WindowsBluetoothTelephonyPlatform : IBluetoothTelephonyPlatform
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint BluetoothServiceDisable = 0;
    private const uint BluetoothServiceEnable = 1;
    private static readonly Guid HandsFreeProfile =
        new("0000111E-0000-1000-8000-00805F9B34FB");
    private static readonly Guid AdvancedAudioSink =
        new("0000110B-0000-1000-8000-00805F9B34FB");

    public BluetoothTelephonyState Read(AirPodsDevice device)
    {
        if (!TryParseAddress(device.Address, out var address))
            return new(false, false, "The AirPods Bluetooth address is invalid.");

        return WithDevice<BluetoothTelephonyState>(
            address,
            (radio, info) =>
            {
                var services = EnumerateServices(radio, ref info, out var error);
                return services is null
                    ? new(false, false, $"Windows could not read Bluetooth services (error {error}).")
                    : new(true, services.Contains(HandsFreeProfile), "Bluetooth telephony state read.");
            },
            new(false, false, "Windows could not find these AirPods on a Bluetooth radio."));
    }

    public BluetoothTelephonyUpdate Set(AirPodsDevice device, bool enabled)
    {
        if (!TryParseAddress(device.Address, out var address))
            return new(false, false, false, "The AirPods Bluetooth address is invalid.");

        return WithDevice<BluetoothTelephonyUpdate>(
            address,
            (radio, info) =>
            {
                var service = HandsFreeProfile;
                var error = NativeMethods.BluetoothSetServiceState(
                    radio,
                    ref info,
                    ref service,
                    enabled ? BluetoothServiceEnable : BluetoothServiceDisable);
                return error == ErrorSuccess
                    ? new(true, true, enabled, "Bluetooth telephony changed.")
                    : new(false, false, !enabled,
                        $"Windows rejected the Bluetooth telephony change (error {error}).");
            },
            new(false, false, false, "Windows could not find these AirPods on a Bluetooth radio."));
    }

    public async Task<AirPodsConnectionAttempt> RestartConnectionAsync(AirPodsDevice device)
    {
        if (!TryParseAddress(device.Address, out var address))
            return new(false, "The AirPods Bluetooth address is invalid.");

        var cycle = await Task.Run(() => WithDevice(
            address,
            (radio, info) =>
            {
                var services = EnumerateServices(radio, ref info, out var readError);
                if (services is null)
                    return new AirPodsConnectionAttempt(false,
                        $"Windows could not read the audio profile before reconnecting (error {readError}).");
                if (!services.Contains(AdvancedAudioSink))
                    return new AirPodsConnectionAttempt(false,
                        "Windows did not expose the AirPods stereo audio profile.");

                var audioSink = AdvancedAudioSink;
                var disableError = NativeMethods.BluetoothSetServiceState(
                    radio,
                    ref info,
                    ref audioSink,
                    BluetoothServiceDisable);
                if (disableError != ErrorSuccess)
                    return new AirPodsConnectionAttempt(false,
                        $"Windows could not disconnect the AirPods audio profile (error {disableError}).");

                Thread.Sleep(500);
                uint enableError = 0;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    enableError = NativeMethods.BluetoothSetServiceState(
                        radio,
                        ref info,
                        ref audioSink,
                        BluetoothServiceEnable);
                    if (enableError == ErrorSuccess) break;
                    Thread.Sleep(300);
                }
                return enableError == ErrorSuccess
                    ? new(true, "Bluetooth audio profile restarted.")
                    : new(false, $"Windows could not restore the AirPods audio profile (error {enableError}).");
            },
            new AirPodsConnectionAttempt(false, "Windows could not find these AirPods on a Bluetooth radio.")));
        if (!cycle.Success) return cycle;

        await Task.Delay(750);
        AirPodsConnectionAttempt reconnect = new(false, "The AirPods audio endpoint did not become ready.");
        for (var attempt = 0; attempt < 8 && !reconnect.Success; attempt++)
        {
            reconnect = WindowsBluetoothAudioConnector.RequestConnect(device);
            if (!reconnect.Success) await Task.Delay(500);
        }
        return reconnect;
    }

    private static HashSet<Guid>? EnumerateServices(
        nint radio,
        ref BluetoothDeviceInfo info,
        out uint error)
    {
        uint count = 0;
        error = NativeMethods.BluetoothEnumerateInstalledServices(radio, ref info, ref count, null);
        if (error is not (ErrorSuccess or ErrorMoreData)) return null;
        if (count == 0) return [];

        var services = new Guid[count];
        error = NativeMethods.BluetoothEnumerateInstalledServices(radio, ref info, ref count, services);
        return error == ErrorSuccess ? services.Take((int)count).ToHashSet() : null;
    }

    private static TResult WithDevice<TResult>(
        ulong address,
        Func<nint, BluetoothDeviceInfo, TResult> action,
        TResult notFound)
    {
        var parameters = new BluetoothFindRadioParams
        {
            Size = (uint)Marshal.SizeOf<BluetoothFindRadioParams>()
        };
        var findHandle = NativeMethods.BluetoothFindFirstRadio(ref parameters, out var radio);
        if (findHandle == nint.Zero) return notFound;

        try
        {
            do
            {
                try
                {
                    var info = BluetoothDeviceInfo.Create(address);
                    if (NativeMethods.BluetoothGetDeviceInfo(radio, ref info) == ErrorSuccess)
                        return action(radio, info);
                }
                finally
                {
                    if (radio != nint.Zero) NativeMethods.CloseHandle(radio);
                }
            } while (NativeMethods.BluetoothFindNextRadio(findHandle, out radio));
        }
        finally
        {
            NativeMethods.BluetoothFindRadioClose(findHandle);
        }

        return notFound;
    }

    private static bool TryParseAddress(string value, out ulong address) =>
        ulong.TryParse(
            value.Replace(":", string.Empty, StringComparison.Ordinal),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out address);

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public uint Size;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;

        public static BluetoothDeviceInfo Create(ulong address) => new()
        {
            Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>(),
            Address = address,
            Name = string.Empty
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    private static class NativeMethods
    {
        [DllImport("bthprops.cpl")]
        public static extern nint BluetoothFindFirstRadio(
            ref BluetoothFindRadioParams parameters,
            out nint radio);

        [DllImport("bthprops.cpl")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindNextRadio(nint findHandle, out nint radio);

        [DllImport("bthprops.cpl")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindRadioClose(nint findHandle);

        [DllImport("bthprops.cpl")]
        public static extern uint BluetoothGetDeviceInfo(nint radio, ref BluetoothDeviceInfo info);

        [DllImport("bthprops.cpl")]
        public static extern uint BluetoothEnumerateInstalledServices(
            nint radio,
            ref BluetoothDeviceInfo info,
            ref uint serviceCount,
            [Out] Guid[]? services);

        [DllImport("bthprops.cpl")]
        public static extern uint BluetoothSetServiceState(
            nint radio,
            ref BluetoothDeviceInfo info,
            ref Guid service,
            uint flags);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint handle);
    }
}
