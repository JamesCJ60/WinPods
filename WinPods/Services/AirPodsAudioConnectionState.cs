using NAudio.CoreAudioApi;
using WinPods.Models;

namespace WinPods.Services;

/// <param name="HasActiveEndpoint">Windows has an active audio output endpoint for the AirPods.</param>
/// <param name="IsInUse">Windows audio is going to that endpoint (default output or driverless EQ target).</param>
public readonly record struct AirPodsAudioRoute(bool HasActiveEndpoint, bool IsInUse);

/// <summary>
/// Reads which AirPods have a live Windows audio endpoint and whether Windows is actually
/// playing through it. One snapshot of the endpoint list serves every device and is reused
/// briefly, because advertisements can arrive several times a second.
/// </summary>
public sealed class AirPodsAudioConnectionState
{
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(2);
    // PKEY_Device_ContainerId: shared by the Bluetooth device and all of its audio endpoints.
    private static readonly PropertyKey ContainerIdKey = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    private readonly object _sync = new();
    private Snapshot? _snapshot;

    public bool IsActive(AirPodsDevice device) => GetRoute(device).HasActiveEndpoint;

    public AirPodsAudioRoute GetRoute(AirPodsDevice device, string? equaliserTargetEndpointId = null)
    {
        var snapshot = GetSnapshot();
        var endpoints = FindDeviceEndpoints(snapshot.Endpoints, device);
        var isInUse = endpoints.Any(endpoint =>
            string.Equals(endpoint.Id, snapshot.DefaultEndpointId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint.Id, equaliserTargetEndpointId, StringComparison.OrdinalIgnoreCase));
        return new(endpoints.Count > 0, isInUse);
    }

    /// <summary>Forces the next read to query Windows again (call after endpoint changes).</summary>
    public void Invalidate()
    {
        lock (_sync) _snapshot = null;
    }

    private Snapshot GetSnapshot()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            if (_snapshot is { } cached && now - cached.TakenAt < SnapshotLifetime) return cached;
            _snapshot = ReadSnapshot(now);
            return _snapshot;
        }
    }

    private static Snapshot ReadSnapshot(DateTimeOffset now)
    {
        var endpoints = new List<EndpointInfo>();
        string? defaultEndpointId = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (endpoint)
                {
                    try
                    {
                        endpoints.Add(new(endpoint.ID, Normalise(endpoint.FriendlyName), ReadContainerId(endpoint)));
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                using var defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultEndpointId = defaultEndpoint.ID;
            }
            catch
            {
                // No default output (e.g. every device disabled).
            }
        }
        catch
        {
        }

        return new(endpoints, defaultEndpointId, now);
    }

    private static List<EndpointInfo> FindDeviceEndpoints(IReadOnlyList<EndpointInfo> endpoints, AirPodsDevice device)
    {
        if (device.ContainerId is { } containerId)
        {
            var byContainer = endpoints.Where(endpoint => endpoint.ContainerId == containerId).ToList();
            if (byContainer.Count > 0) return byContainer;
        }

        // Name matching is only a fallback: an endpoint that carries a different container ID
        // belongs to some other device (e.g. a second pair of AirPods with the same name).
        var name = Normalise(device.Name);
        var model = Normalise(device.Model);
        return endpoints.Where(endpoint =>
                (device.ContainerId is null || endpoint.ContainerId is null) &&
                ((name.Length > 0 && endpoint.NormalisedName.Contains(name, StringComparison.Ordinal)) ||
                 (model.Length > 0 && endpoint.NormalisedName.Contains(model, StringComparison.Ordinal))))
            .ToList();
    }

    private static Guid? ReadContainerId(MMDevice endpoint)
    {
        try
        {
            var properties = endpoint.Properties;
            return properties.Contains(ContainerIdKey) && properties[ContainerIdKey].Value is Guid containerId
                ? containerId
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Normalise(string value) => new(value
        .ToLowerInvariant()
        .Where(character => char.IsLetterOrDigit(character))
        .ToArray());

    private sealed record EndpointInfo(string Id, string NormalisedName, Guid? ContainerId);

    private sealed record Snapshot(IReadOnlyList<EndpointInfo> Endpoints, string? DefaultEndpointId, DateTimeOffset TakenAt);
}
