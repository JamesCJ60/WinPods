using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WinPods.Services;

/// <summary>
/// Raises <see cref="Changed"/> when Windows adds, removes, enables or disables an audio output,
/// or switches the default output. Events arrive on a COM worker thread and often in bursts
/// (one per role), so subscribers should marshal and coalesce them.
/// </summary>
public sealed class AudioEndpointChangeWatcher : IMMNotificationClient, IDisposable
{
    private MMDeviceEnumerator? _enumerator;

    public event EventHandler? Changed;

    public void Start()
    {
        if (_enumerator is not null) return;
        var enumerator = new MMDeviceEnumerator();
        try
        {
            enumerator.RegisterEndpointNotificationCallback(this);
            _enumerator = enumerator;
        }
        catch
        {
            enumerator.Dispose();
        }
    }

    public void Dispose()
    {
        if (_enumerator is null) return;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
        }
        _enumerator.Dispose();
        _enumerator = null;
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => Raise();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => Raise();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => Raise();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render) Raise();
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Property changes (volume, format, names...) are far too chatty and don't affect routing.
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Never let a subscriber exception escape back into the audio service.
        }
    }
}
