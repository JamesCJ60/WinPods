namespace WinPods.Services;

internal sealed class SmoothBiquadFilter
{
    private const float Smoothing = 0.0025f;
    private Coefficients _current;
    private Coefficients _target;
    private float _x1;
    private float _x2;
    private float _y1;
    private float _y2;

    private SmoothBiquadFilter(Coefficients coefficients)
    {
        _current = coefficients;
        _target = coefficients;
    }

    public static SmoothBiquadFilter LowShelf(float sampleRate, float frequency, float slope, float gain) =>
        new(CreateShelf(sampleRate, frequency, slope, gain, false));

    public static SmoothBiquadFilter HighShelf(float sampleRate, float frequency, float slope, float gain) =>
        new(CreateShelf(sampleRate, frequency, slope, gain, true));

    public static SmoothBiquadFilter Peaking(float sampleRate, float frequency, float q, float gain) =>
        new(CreatePeaking(sampleRate, frequency, q, gain));

    public void SetLowShelf(float sampleRate, float frequency, float slope, float gain) =>
        _target = CreateShelf(sampleRate, frequency, slope, gain, false);

    public void SetHighShelf(float sampleRate, float frequency, float slope, float gain) =>
        _target = CreateShelf(sampleRate, frequency, slope, gain, true);

    public void SetPeaking(float sampleRate, float frequency, float q, float gain) =>
        _target = CreatePeaking(sampleRate, frequency, q, gain);

    public float Transform(float input)
    {
        _current = _current.MoveTowards(_target, Smoothing);
        var output = _current.B0 * input + _current.B1 * _x1 + _current.B2 * _x2
            - _current.A1 * _y1 - _current.A2 * _y2;
        _x2 = _x1;
        _x1 = input;
        _y2 = _y1;
        _y1 = output;
        return output;
    }

    private static Coefficients CreatePeaking(float sampleRate, float frequency, float q, float gain)
    {
        var a = Math.Pow(10, gain / 40d);
        var omega = 2 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(omega) / (2 * q);
        var cosine = Math.Cos(omega);
        var a0 = 1 + alpha / a;
        return Normalise(
            1 + alpha * a,
            -2 * cosine,
            1 - alpha * a,
            a0,
            -2 * cosine,
            1 - alpha / a);
    }

    private static Coefficients CreateShelf(float sampleRate, float frequency, float slope, float gain, bool high)
    {
        var a = Math.Pow(10, gain / 40d);
        var omega = 2 * Math.PI * frequency / sampleRate;
        var cosine = Math.Cos(omega);
        var alpha = Math.Sin(omega) / 2 * Math.Sqrt((a + 1 / a) * (1 / slope - 1) + 2);
        var root = 2 * Math.Sqrt(a) * alpha;

        if (high)
        {
            return Normalise(
                a * ((a + 1) + (a - 1) * cosine + root),
                -2 * a * ((a - 1) + (a + 1) * cosine),
                a * ((a + 1) + (a - 1) * cosine - root),
                (a + 1) - (a - 1) * cosine + root,
                2 * ((a - 1) - (a + 1) * cosine),
                (a + 1) - (a - 1) * cosine - root);
        }

        return Normalise(
            a * ((a + 1) - (a - 1) * cosine + root),
            2 * a * ((a - 1) - (a + 1) * cosine),
            a * ((a + 1) - (a - 1) * cosine - root),
            (a + 1) + (a - 1) * cosine + root,
            -2 * ((a - 1) + (a + 1) * cosine),
            (a + 1) + (a - 1) * cosine - root);
    }

    private static Coefficients Normalise(double b0, double b1, double b2, double a0, double a1, double a2) =>
        new((float)(b0 / a0), (float)(b1 / a0), (float)(b2 / a0), (float)(a1 / a0), (float)(a2 / a0));

    private readonly record struct Coefficients(float B0, float B1, float B2, float A1, float A2)
    {
        public Coefficients MoveTowards(Coefficients target, float amount) => new(
            B0 + (target.B0 - B0) * amount,
            B1 + (target.B1 - B1) * amount,
            B2 + (target.B2 - B2) * amount,
            A1 + (target.A1 - A1) * amount,
            A2 + (target.A2 - A2) * amount);
    }
}
