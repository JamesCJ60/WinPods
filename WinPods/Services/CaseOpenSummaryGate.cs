using WinPods.Models;

namespace WinPods.Services;

public sealed class CaseOpenSummaryGate
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PromptCooldown = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, Observation> _observations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastPrompts =
        new(StringComparer.OrdinalIgnoreCase);

    public bool ShouldShow(string deviceId, AirPodsAdvertisement advertisement)
    {
        if (!advertisement.IsCaseOpen) return false;

        var observation = _observations.GetValueOrDefault(deviceId);
        var count = observation is not null &&
                    advertisement.LastSeen - observation.LastSeen <= ConfirmationWindow
            ? observation.Count + 1
            : 1;
        _observations[deviceId] = new(count, advertisement.LastSeen);
        if (count < 2) return false;

        _observations.Remove(deviceId);
        if (_lastPrompts.TryGetValue(deviceId, out var lastPrompt) &&
            advertisement.LastSeen - lastPrompt < PromptCooldown)
            return false;

        _lastPrompts[deviceId] = advertisement.LastSeen;
        return true;
    }

    private sealed record Observation(int Count, DateTimeOffset LastSeen);
}
