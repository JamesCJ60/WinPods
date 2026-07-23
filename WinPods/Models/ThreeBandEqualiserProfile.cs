namespace WinPods.Models;

public sealed record ThreeBandEqualiserProfile(double Low, double Mid, double High)
{
    public const double MinimumGain = -18;
    public const double MaximumGain = 18;

    public static ThreeBandEqualiserProfile Recommended { get; } = new(0, 0, 0);

    public bool IsCustom { get; init; }

    public ThreeBandEqualiserProfile Normalised() => new(
        Math.Round(Math.Clamp(Low, MinimumGain, MaximumGain)),
        Math.Round(Math.Clamp(Mid, MinimumGain, MaximumGain)),
        Math.Round(Math.Clamp(High, MinimumGain, MaximumGain)))
    {
        IsCustom = IsCustom
    };
}
