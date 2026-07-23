using System.Runtime.InteropServices;
using System.Security;
using Vanara.PInvoke;
using WinPods.Models;
using static Vanara.PInvoke.CoreAudio;

namespace WinPods.Services;

public static class WindowsBluetoothAudioConnector
{
    private static readonly Guid BluetoothAudioPropertySet = new("7fa06c40-b8f6-4c7e-8556-e8c33a12e54d");

    public static AirPodsConnectionAttempt RequestConnect(AirPodsDevice device)
    {
        if (device.ContainerId is not { } containerId)
            return new(false, "Windows did not expose the AirPods device container.");

        try
        {
            var endpoints = FindBluetoothAudioEndpoints(containerId).ToArray();
            if (endpoints.Length == 0)
                return new(false, "Windows did not expose a reconnectable AirPods audio profile.");

            var accepted = 0;
            foreach (var endpoint in endpoints)
            {
                var property = new KsProperty(BluetoothAudioPropertySet, 0, 1);
                var returned = 0;
                if (endpoint.KsProperty(ref property, Marshal.SizeOf<KsProperty>(), nint.Zero, 0, ref returned) >= 0)
                    accepted++;
            }

            return accepted > 0
                ? new(true, $"Bluetooth audio connection requested for {device.Name}.")
                : new(false, "Windows rejected the AirPods Bluetooth audio connection request.");
        }
        catch (Exception exception)
        {
            return new(false, $"Windows could not request the Bluetooth audio connection: {exception.Message}");
        }
    }

    public static int GetTargetCount(AirPodsDevice device) =>
        device.ContainerId is { } containerId ? FindBluetoothAudioEndpoints(containerId).Count() : 0;

    private static IEnumerable<IKsControl> FindBluetoothAudioEndpoints(Guid containerId)
    {
        var enumerator = new IMMDeviceEnumerator();
        var collection = enumerator.EnumAudioEndpoints(EDataFlow.eAll, DEVICE_STATE.DEVICE_STATEMASK_ALL);
        if (collection is null) yield break;
        for (uint index = 0; index < collection.GetCount(); index++)
        {
            collection.Item(index, out var audioEndpoint);
            if (audioEndpoint is null) continue;
            if (ReadContainerId(audioEndpoint) != containerId) continue;

            foreach (var connector in EnumerateConnectors(audioEndpoint))
            {
                IPart? connectedPart;
                try
                {
                    connectedPart = connector.GetConnectedTo() as IPart;
                }
                catch
                {
                    continue;
                }
                if (connectedPart is null) continue;

                var topologyObject = connectedPart.GetTopologyObject();
                var rawDeviceId = topologyObject?.GetDeviceId();
                if (rawDeviceId is null) continue;
                var deviceId = rawDeviceId.ToString();
                if (string.IsNullOrWhiteSpace(deviceId)) continue;
                if (!deviceId.StartsWith(@"{2}.\\?\bth", StringComparison.OrdinalIgnoreCase)) continue;

                var bluetoothEndpoint = enumerator.GetDevice(deviceId);
                if (bluetoothEndpoint is null) continue;
                bluetoothEndpoint.Activate(typeof(IKsControl).GUID, Ole32.CLSCTX.CLSCTX_ALL, null, out var control);
                if (control is IKsControl ksControl) yield return ksControl;
            }
        }
    }

    private static IEnumerable<IConnector> EnumerateConnectors(IMMDevice endpoint)
    {
        endpoint.Activate(typeof(IDeviceTopology).GUID, Ole32.CLSCTX.CLSCTX_ALL, null, out var value);
        if (value is not IDeviceTopology topology) yield break;
        for (uint index = 0; index < topology.GetConnectorCount(); index++)
        {
            var connector = topology.GetConnector(index);
            if (connector is not null) yield return connector;
        }
    }

    private static Guid? ReadContainerId(IMMDevice endpoint)
    {
        var store = endpoint.OpenPropertyStore(STGM.STGM_READ);
        if (store is null) return null;
        var value = store.GetValue(Ole32.PROPERTYKEY.System.Devices.ContainerId);
        return value is Guid identifier ? identifier : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KsProperty(Guid set, uint identifier, uint flags)
    {
        public Guid Set = set;
        public uint Identifier = identifier;
        public uint Flags = flags;
    }

    [ComImport]
    [SuppressUnmanagedCodeSecurity]
    [Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IKsControl
    {
        [PreserveSig]
        int KsProperty(
            [In] ref KsProperty property,
            [In] int propertyLength,
            [In, Out] nint propertyData,
            [In] int dataLength,
            [In, Out] ref int bytesReturned);

        [PreserveSig]
        int KsMethod(
            [In] ref KsProperty method,
            [In] int methodLength,
            [In, Out] nint methodData,
            [In] int dataLength,
            [In, Out] ref int bytesReturned);

        [PreserveSig]
        int KsEvent(
            [In] ref KsProperty eventProperty,
            [In] int eventLength,
            [In, Out] nint eventData,
            [In] int dataLength,
            [In, Out] ref int bytesReturned);
    }
}
