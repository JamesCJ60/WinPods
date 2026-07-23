using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace WinPods.Services;

public sealed class SystemAudioSpectrumService : IDisposable
{
    private const int FftLength = 2048;
    private const int BandCount = 52;
    private readonly Complex[] _fft = new Complex[FftLength];
    private readonly double[] _previous = new double[BandCount];
    private readonly object _sync = new();
    private WasapiLoopbackCapture? _capture;
    private MMDevice? _captureDevice;
    private string? _boundDeviceId;
    private System.Threading.Timer? _watchdog;
    private int _fftPosition;
    private bool _intentionalStop;
    private bool _disposed;

    public event EventHandler<IReadOnlyList<double>>? SpectrumAvailable;

    public string Start()
    {
        lock (_sync)
        {
            if (_disposed) return "Live analyser stopped.";
            var message = EnsureCapture();
            _watchdog ??= new System.Threading.Timer(
                _ => EnsureCapture(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            return message;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _watchdog?.Dispose();
            _watchdog = null;
            StopCapture();
        }
    }

    private string EnsureCapture()
    {
        lock (_sync)
        {
            if (_disposed) return "Live analyser stopped.";

            MMDevice device;
            string currentId;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                currentId = device.ID;
            }
            catch (Exception exception)
            {
                return $"Live analyser unavailable: {exception.Message}";
            }

            if (_capture is not null && string.Equals(currentId, _boundDeviceId, StringComparison.Ordinal))
            {
                device.Dispose();
                return "Live system-output analyser active.";
            }

            StopCapture();
            try
            {
                _captureDevice = device;
                _capture = new WasapiLoopbackCapture(device);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _fftPosition = 0;
                _capture.StartRecording();
                _boundDeviceId = currentId;
                return "Live system-output analyser active.";
            }
            catch (Exception exception)
            {
                StopCapture();
                return $"Live analyser unavailable: {exception.Message}";
            }
        }
    }

    private void StopCapture()
    {
        if (_capture is not null)
        {
            _intentionalStop = true;
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch { }
            try { _capture.Dispose(); } catch { }
            _capture = null;
            _intentionalStop = false;
        }
        if (_captureDevice is not null)
        {
            try { _captureDevice.Dispose(); } catch { }
            _captureDevice = null;
        }
        _boundDeviceId = null;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            if (_disposed || _intentionalStop) return;
            _capture = null;
            _boundDeviceId = null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture) return;
        var format = capture.WaveFormat;
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var frameSize = bytesPerSample * Math.Max(1, format.Channels);

        for (var offset = 0; offset + frameSize <= e.BytesRecorded; offset += frameSize)
        {
            var sample = 0f;
            for (var channel = 0; channel < format.Channels; channel++)
                sample += ReadSample(e.Buffer, offset + channel * bytesPerSample, format);
            sample /= Math.Max(1, format.Channels);

            _fft[_fftPosition].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPosition, FftLength));
            _fft[_fftPosition].Y = 0;
            if (++_fftPosition < FftLength) continue;

            _fftPosition = 0;
            FastFourierTransform.FFT(true, 11, _fft);
            PublishSpectrum(format.SampleRate);
        }
    }

    private void PublishSpectrum(int sampleRate)
    {
        var levels = new double[BandCount];
        const double minimumFrequency = 35;
        var maximumFrequency = Math.Min(18000, sampleRate / 2d);
        for (var band = 0; band < BandCount; band++)
        {
            var startFrequency = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, band / (double)BandCount);
            var endFrequency = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, (band + 1d) / BandCount);
            var firstBin = Math.Clamp((int)(startFrequency * FftLength / sampleRate), 1, FftLength / 2 - 1);
            var lastBin = Math.Clamp((int)Math.Ceiling(endFrequency * FftLength / sampleRate), firstBin + 1, FftLength / 2);
            var magnitude = 0d;
            for (var bin = firstBin; bin < lastBin; bin++)
                magnitude = Math.Max(magnitude, Math.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y));

            var decibels = 20 * Math.Log10(Math.Max(1e-8, magnitude));
            var normalised = Math.Clamp((decibels + 78) / 70, 0, 1);
            levels[band] = Math.Max(normalised, _previous[band] * 0.82);
            _previous[band] = levels[band];
        }
        SpectrumAvailable?.Invoke(this, levels);
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            return BitConverter.ToSingle(buffer, offset);
        if (format.BitsPerSample == 16)
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        if (format.BitsPerSample == 24)
        {
            var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
            if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
            return value / 8388608f;
        }
        if (format.BitsPerSample == 32)
            return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        return 0;
    }
}
