using NAudio.Wave;

namespace WinPods.Services;

public sealed class ThreeBandEqSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly object _filterLock = new();
    private SmoothBiquadFilter[] _lowFilters = [];
    private SmoothBiquadFilter[] _midFilters = [];
    private SmoothBiquadFilter[] _highFilters = [];
    private float _preampGain = 1;
    private float _targetPreampGain = 1;
    private float _limiterGain = 1;
    private long _processedSamples;

    public long ProcessedSamples => Interlocked.Read(ref _processedSamples);

    public ThreeBandEqSampleProvider(ISampleProvider source, Models.ThreeBandEqualiserProfile profile)
    {
        _source = source;
        Update(profile);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void Update(Models.ThreeBandEqualiserProfile profile)
    {
        profile = profile.Normalised();
        var channels = Math.Max(1, WaveFormat.Channels);
        var sampleRate = WaveFormat.SampleRate;
        var highestBoost = Math.Max(profile.Low, Math.Max(profile.Mid, profile.High));
        var reservedHeadroom = Math.Max(0, highestBoost - 12);

        lock (_filterLock)
        {
            _targetPreampGain = (float)Math.Pow(10, -reservedHeadroom / 20);
            if (_lowFilters.Length != channels)
            {
                _lowFilters = Enumerable.Range(0, channels)
                    .Select(_ => SmoothBiquadFilter.LowShelf(sampleRate, 140, 0.8f, (float)profile.Low))
                    .ToArray();
                _midFilters = Enumerable.Range(0, channels)
                    .Select(_ => SmoothBiquadFilter.Peaking(sampleRate, 1100, 0.85f, (float)profile.Mid))
                    .ToArray();
                _highFilters = Enumerable.Range(0, channels)
                    .Select(_ => SmoothBiquadFilter.HighShelf(sampleRate, 7500, 0.8f, (float)profile.High))
                    .ToArray();
                return;
            }

            foreach (var filter in _lowFilters) filter.SetLowShelf(sampleRate, 140, 0.8f, (float)profile.Low);
            foreach (var filter in _midFilters) filter.SetPeaking(sampleRate, 1100, 0.85f, (float)profile.Mid);
            foreach (var filter in _highFilters) filter.SetHighShelf(sampleRate, 7500, 0.8f, (float)profile.High);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        lock (_filterLock)
        {
            var channels = Math.Max(1, WaveFormat.Channels);
            for (var frameOffset = 0; frameOffset < read; frameOffset += channels)
            {
                _preampGain += (_targetPreampGain - _preampGain) * 0.0025f;
                var samplesInFrame = Math.Min(channels, read - frameOffset);
                var peak = 0f;
                for (var channel = 0; channel < samplesInFrame; channel++)
                {
                    var index = offset + frameOffset + channel;
                    var sample = _lowFilters[channel].Transform(buffer[index]);
                    sample = _midFilters[channel].Transform(sample);
                    sample = _highFilters[channel].Transform(sample);
                    sample *= _preampGain;
                    buffer[index] = sample;
                    peak = Math.Max(peak, Math.Abs(sample));
                }

                if (peak > 0.98f)
                    _limiterGain = Math.Min(_limiterGain, 0.98f / peak);
                else
                    _limiterGain += (1f - _limiterGain) * 0.0008f;

                for (var channel = 0; channel < samplesInFrame; channel++)
                {
                    var index = offset + frameOffset + channel;
                    buffer[index] = Math.Clamp(buffer[index] * _limiterGain, -1f, 1f);
                }
            }
        }
        Interlocked.Add(ref _processedSamples, read);
        return read;
    }
}
