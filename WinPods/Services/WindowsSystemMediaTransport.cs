using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace WinPods.Services;

public sealed class WindowsSystemMediaTransport : ISystemMediaTransport
{
    private const byte MediaPlayPauseKey = 0xB3;
    private const uint KeyUp = 0x0002;
    private readonly SemaphoreSlim _managerGate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _pausedSourceId;
    private bool _pausedWithMediaKey;

    public async Task InitialiseAsync() => _ = await GetManagerAsync();

    public async Task<bool> PauseAsync()
    {
        try
        {
            var manager = await GetManagerAsync();
            if (manager is null)
            {
                return await PauseWithMediaKeyIfAudibleAsync();
            }
            var session = FindPlayingSession(manager);
            if (session is null)
            {
                return await PauseWithMediaKeyIfAudibleAsync();
            }

            _pausedSourceId = session.SourceAppUserModelId;
            if (await session.TryPauseAsync())
            {
                _pausedWithMediaKey = false;
                return true;
            }

            if (await session.TryTogglePlayPauseAsync())
            {
                _pausedWithMediaKey = false;
                return true;
            }

            SendPlayPauseKey();
            _pausedWithMediaKey = true;
            return true;
        }
        catch
        {
            return await PauseWithMediaKeyIfAudibleAsync();
        }
    }

    public async Task<bool> PlayAsync()
    {
        try
        {
            var manager = await GetManagerAsync();
            if (manager is null)
            {
                if (!_pausedWithMediaKey) return false;
                SendPlayPauseKey();
                ClearPausedSession();
                return true;
            }
            var session = FindPausedSession(manager);
            if (session is not null && await session.TryPlayAsync())
            {
                ClearPausedSession();
                return true;
            }

            if (session is not null && await session.TryTogglePlayPauseAsync())
            {
                ClearPausedSession();
                return true;
            }

            if (!_pausedWithMediaKey) return false;
            SendPlayPauseKey();
            ClearPausedSession();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HasActivePlaybackAsync()
    {
        try
        {
            var manager = await GetManagerAsync();
            return manager is not null && FindPlayingSession(manager) is not null || IsOutputAudible();
        }
        catch
        {
            return IsOutputAudible();
        }
    }

    private static GlobalSystemMediaTransportControlsSession? FindPlayingSession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var current = manager.GetCurrentSession();
        if (IsPlaying(current)) return current;
        foreach (var session in manager.GetSessions())
        {
            if (IsPlaying(session)) return session;
        }
        return null;
    }

    private GlobalSystemMediaTransportControlsSession? FindPausedSession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (string.IsNullOrWhiteSpace(_pausedSourceId)) return manager.GetCurrentSession();
        foreach (var session in manager.GetSessions())
        {
            if (string.Equals(session.SourceAppUserModelId, _pausedSourceId, StringComparison.Ordinal))
                return session;
        }
        return null;
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession? session) =>
        session?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private async Task<GlobalSystemMediaTransportControlsSessionManager?> GetManagerAsync()
    {
        if (_manager is not null) return _manager;
        await _managerGate.WaitAsync();
        try
        {
            if (_manager is not null) return _manager;
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            return _manager;
        }
        catch
        {
            return null;
        }
        finally
        {
            _managerGate.Release();
        }
    }

    private async Task<bool> PauseWithMediaKeyIfAudibleAsync()
    {
        for (var sample = 0; sample < 6; sample++)
        {
            if (IsOutputAudible())
            {
                _pausedSourceId = null;
                _pausedWithMediaKey = true;
                SendPlayPauseKey();
                return true;
            }
            await Task.Delay(40);
        }
        return false;
    }

    private static bool IsOutputAudible()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return endpoint.AudioMeterInformation.MasterPeakValue > 0.001f;
        }
        catch
        {
            return false;
        }
    }

    private void ClearPausedSession()
    {
        _pausedSourceId = null;
        _pausedWithMediaKey = false;
    }

    private static void SendPlayPauseKey()
    {
        keybd_event(MediaPlayPauseKey, 0, 0, UIntPtr.Zero);
        keybd_event(MediaPlayPauseKey, 0, KeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
