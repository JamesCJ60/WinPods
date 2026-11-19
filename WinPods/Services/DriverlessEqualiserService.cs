using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using System.Text.Json;
using WinPods.Models;

namespace WinPods.Services;

public sealed record DriverlessEqualiserAvailability(bool IsAvailable, string Message);

public sealed class DriverlessEqualiserService : IDisposable
{
    private static readonly string RecoveryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinPods", "audio-route-recovery.json");
    private readonly object _sync = new();
    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private ThreeBandEqSampleProvider? _equaliser;
    private MMDevice? _sourceDevice;
    private MMDevice? _targetDevice;
    private volatile string? _targetEndpointId;
    private bool _sourceWasMuted;
    private bool _targetWasMuted;
    private string? _originalConsoleEndpointId;
    private string? _originalMultimediaEndpointId;
    private bool _disposed;
    private long _capturedBytes;

    public DriverlessEqualiserService() => RecoverInterruptedRoute();

    public bool IsRunning { get; private set; }

    /// <summary>The AirPods endpoint the EQ renders to while running; Windows' default output is a different device then.</summary>
    public string? TargetEndpointId => _targetEndpointId;

    public string RouteDescription { get; private set; } = string.Empty;

    public string OutputFormatDescription { get; private set; } = string.Empty;

    public long CapturedBytes => Interlocked.Read(ref _capturedBytes);

    public long ProcessedSamples => _equaliser?.ProcessedSamples ?? 0;

    public DriverlessEqualiserAvailability Probe(AirPodsDevice? airPods)
    {
        if (airPods is null)
            return new(false, "Select connected AirPods first.");
        if (!airPods.IsConnected)
            return new(false, "Connect the selected AirPods to enable driverless EQ.");

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            try
            {
                var route = FindRoute(enumerator, devices, airPods);
                return route is null
                    ? new(false, "Driverless EQ needs an active stereo output in addition to the AirPods.")
                    : new(true, $"Ready to route {route.Value.Source.FriendlyName} to {route.Value.Target.FriendlyName}.");
            }
            finally
            {
                foreach (var device in devices) device.Dispose();
            }
        }
        catch (Exception exception)
        {
            return new(false, $"Audio routing unavailable: {exception.Message}");
        }
    }

    public string Start(AirPodsDevice? airPods, ThreeBandEqualiserProfile profile)
    {
        lock (_sync)
        {
            if (_disposed) return "The audio engine has already stopped.";
            if (IsRunning) return $"Driverless EQ is active: {RouteDescription}.";
            if (airPods is null || !airPods.IsConnected) return "Connect and select the AirPods first.";

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                var route = FindRoute(enumerator, devices, airPods);
                if (route is null)
                {
                    foreach (var device in devices) device.Dispose();
                    return "Driverless EQ needs an active stereo output in addition to the AirPods.";
                }

                _sourceDevice = route.Value.Source;
                _targetDevice = route.Value.Target;
                foreach (var device in devices)
                {
                    if (!ReferenceEquals(device, _sourceDevice) && !ReferenceEquals(device, _targetDevice))
                        device.Dispose();
                }

                using var originalConsole = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                using var originalMultimedia = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _originalConsoleEndpointId = originalConsole.ID;
                _originalMultimediaEndpointId = originalMultimedia.ID;

                _sourceWasMuted = _sourceDevice.AudioEndpointVolume.Mute;
                _targetWasMuted = _targetDevice.AudioEndpointVolume.Mute;
                SaveRecoveryState();

                WindowsDefaultAudioEndpoint.Set(_sourceDevice.ID,
                    WindowsDefaultAudioEndpoint.EndpointRole.Console,
                    WindowsDefaultAudioEndpoint.EndpointRole.Multimedia);

                _targetDevice.AudioEndpointVolume.Mute = false;

                _capture = new WasapiLoopbackCapture(_sourceDevice);
                _buffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(350),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                var targetFormat = _targetDevice.AudioClient.MixFormat;
                ISampleProvider formatMatchedSource = _buffer.ToSampleProvider();
                if (formatMatchedSource.WaveFormat.SampleRate != targetFormat.SampleRate)
                {
                    formatMatchedSource = new WdlResamplingSampleProvider(formatMatchedSource, targetFormat.SampleRate);
                }

                _equaliser = new ThreeBandEqSampleProvider(formatMatchedSource, profile);
                _output = new WasapiOut(_targetDevice, AudioClientShareMode.Shared, true, 70);
                _output.Init(_equaliser.ToWaveProvider());
                _capture.DataAvailable += OnCaptureDataAvailable;

                Interlocked.Exchange(ref _capturedBytes, 0);

                _output.Play();
                _capture.StartRecording();
                _sourceDevice.AudioEndpointVolume.Mute = true;

                OutputFormatDescription = FormatDescription(targetFormat);
                RouteDescription = $"{ShortName(_sourceDevice.FriendlyName)} → {ShortName(_targetDevice.FriendlyName)} · {OutputFormatDescription}";
                _targetEndpointId = _targetDevice.ID;
                IsRunning = true;
                return $"Driverless EQ active: {RouteDescription}. The curve is applied live.";
            }
            catch (Exception exception)
            {
                StopCore();
                return $"Driverless EQ could not start: {exception.Message}";
            }
        }
    }

    public string Stop()
    {
        lock (_sync)
        {
            if (!IsRunning && _capture is null && _output is null) return "Driverless EQ is off.";
            StopCore();
            return "Driverless EQ stopped and the previous Windows output was restored.";
        }
    }

    public void Update(ThreeBandEqualiserProfile profile)
    {
        lock (_sync) _equaliser?.Update(profile);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            StopCore();
            _disposed = true;
        }
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs args)
    {
        _buffer?.AddSamples(args.Buffer, 0, args.BytesRecorded);
        Interlocked.Add(ref _capturedBytes, args.BytesRecorded);
    }

    private void StopCore()
    {
        IsRunning = false;
        _targetEndpointId = null;
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnCaptureDataAvailable;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }
        if (_output is not null)
        {
            try { _output.Stop(); } catch { }
            _output.Dispose();
            _output = null;
        }

        try
        {
            if (_sourceDevice is not null) _sourceDevice.AudioEndpointVolume.Mute = _sourceWasMuted;
            if (_targetDevice is not null) _targetDevice.AudioEndpointVolume.Mute = _targetWasMuted;
        }
        catch { }

        try
        {
            if (!string.IsNullOrWhiteSpace(_originalConsoleEndpointId))
                WindowsDefaultAudioEndpoint.Set(_originalConsoleEndpointId, WindowsDefaultAudioEndpoint.EndpointRole.Console);
            if (!string.IsNullOrWhiteSpace(_originalMultimediaEndpointId))
                WindowsDefaultAudioEndpoint.Set(_originalMultimediaEndpointId, WindowsDefaultAudioEndpoint.EndpointRole.Multimedia);
        }
        catch { }

        _sourceDevice?.Dispose();
        _targetDevice?.Dispose();
        _sourceDevice = null;
        _targetDevice = null;
        _buffer = null;
        _equaliser = null;
        _originalConsoleEndpointId = null;
        _originalMultimediaEndpointId = null;
        RouteDescription = string.Empty;
        ClearRecoveryState();
    }

    private void SaveRecoveryState()
    {
        var directory = Path.GetDirectoryName(RecoveryPath)!;
        Directory.CreateDirectory(directory);
        var state = new AudioRouteRecovery(
            _sourceDevice!.ID,
            _targetDevice!.ID,
            _sourceWasMuted,
            _targetWasMuted,
            _originalConsoleEndpointId,
            _originalMultimediaEndpointId);
        File.WriteAllText(RecoveryPath, JsonSerializer.Serialize(state));
    }

    private static void RecoverInterruptedRoute()
    {
        if (!File.Exists(RecoveryPath)) return;
        try
        {
            var state = JsonSerializer.Deserialize<AudioRouteRecovery>(File.ReadAllText(RecoveryPath));
            if (state is null) return;
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                using var source = enumerator.GetDevice(state.SourceEndpointId);
                source.AudioEndpointVolume.Mute = state.SourceWasMuted;
            }
            catch { }
            try
            {
                using var target = enumerator.GetDevice(state.TargetEndpointId);
                target.AudioEndpointVolume.Mute = state.TargetWasMuted;
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(state.OriginalConsoleEndpointId))
                WindowsDefaultAudioEndpoint.Set(state.OriginalConsoleEndpointId, WindowsDefaultAudioEndpoint.EndpointRole.Console);
            if (!string.IsNullOrWhiteSpace(state.OriginalMultimediaEndpointId))
                WindowsDefaultAudioEndpoint.Set(state.OriginalMultimediaEndpointId, WindowsDefaultAudioEndpoint.EndpointRole.Multimedia);
        }
        catch { }
        finally
        {
            ClearRecoveryState();
        }
    }

    private static void ClearRecoveryState()
    {
        try { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); }
        catch { }
    }

    private static (MMDevice Source, MMDevice Target)? FindRoute(
        MMDeviceEnumerator enumerator, IReadOnlyList<MMDevice> devices, AirPodsDevice airPods)
    {
        var target = devices
            .Where(IsAirPodsEndpoint)
            .OrderByDescending(device => TargetScore(device, airPods))
            .FirstOrDefault();
        if (target is null) return null;

        MMDevice? currentDefault = null;
        try { currentDefault = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { }

        var targetFormat = SafeMixFormat(target);
        var targetChannels = targetFormat?.Channels ?? 0;
        var candidates = devices.Where(device =>
                !ReferenceEquals(device, target) &&
                !IsAirPodsEndpoint(device) &&
                SafeChannelCount(device) == targetChannels)
            .ToList();
        var source = candidates
            .OrderByDescending(device => SourceScore(device, targetFormat, currentDefault?.ID))
            .FirstOrDefault();
        currentDefault?.Dispose();
        return source is null ? null : (source, target);
    }

    private static int TargetScore(MMDevice device, AirPodsDevice airPods)
    {
        var endpoint = Normalise(device.FriendlyName);
        var name = Normalise(airPods.Name);
        var model = Normalise(airPods.Model);
        var score = endpoint.Contains(name, StringComparison.Ordinal) ? 200 : 0;
        if (endpoint.Contains(model, StringComparison.Ordinal)) score += 100;
        if (endpoint.Contains("airpods", StringComparison.Ordinal)) score += 30;
        if (SafeChannelCount(device) == 2) score += 20;
        if (endpoint.Contains("handsfree", StringComparison.Ordinal)) score -= 150;
        return score;
    }

    private static int SourceScore(MMDevice device, WaveFormat? targetFormat, string? currentDefaultId)
    {
        var name = device.FriendlyName;
        var score = 0;
        var format = SafeMixFormat(device);
        if (device.ID == currentDefaultId) score += 120;
        if (format is not null && targetFormat is not null)
        {
            if (format.SampleRate == targetFormat.SampleRate) score += 300;
            if (format.BitsPerSample == targetFormat.BitsPerSample) score += 80;
            if (format.Encoding == targetFormat.Encoding) score += 40;
        }
        if (name.Contains("Speaker", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (name.Contains("Realtek", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (name.Contains("HDMI", StringComparison.OrdinalIgnoreCase) || name.Contains("Display", StringComparison.OrdinalIgnoreCase)) score -= 10;
        return score;
    }

    private static bool IsAirPodsEndpoint(MMDevice device) =>
        device.FriendlyName.Contains("AirPods", StringComparison.OrdinalIgnoreCase);

    private static int SafeChannelCount(MMDevice device)
    {
        try { return device.AudioClient.MixFormat.Channels; }
        catch { return 0; }
    }

    private static WaveFormat? SafeMixFormat(MMDevice device)
    {
        try { return device.AudioClient.MixFormat; }
        catch { return null; }
    }

    private static string FormatDescription(WaveFormat format) =>
        $"{format.SampleRate / 1000d:0.#} kHz, {format.BitsPerSample}-bit, {format.Channels} ch";

    private static string Normalise(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string ShortName(string value) => value.Length <= 34 ? value : value[..31] + "...";

    private sealed record AudioRouteRecovery(
        string SourceEndpointId,
        string TargetEndpointId,
        bool SourceWasMuted,
        bool TargetWasMuted,
        string? OriginalConsoleEndpointId,
        string? OriginalMultimediaEndpointId);
}
