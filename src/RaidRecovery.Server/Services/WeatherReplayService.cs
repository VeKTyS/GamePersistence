using RaidRecovery.Server.Storage;
using SPTarkov.Server.Core.Models.Spt.Weather;

namespace RaidRecovery.Server.Services;

/// <summary>
/// Same idea as for the loot: the weather of a raid is drawn by the server, so to find it again we keep what
/// was served and serve it again. The game asks for the weather more than once during a raid: only the first
/// request after a recovery gets the kept one, the following ones get the server's and become the reference.
/// </summary>
public sealed class WeatherReplayService(IWeatherStore store)
{
    private readonly Lock _gate = new();

    // Profiles whose next weather request belongs to a resumed raid. In memory only, like the loot.
    private readonly HashSet<string> _armed = [];

    public void Arm(string profileId)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return;
        }

        lock (_gate)
        {
            _armed.Add(profileId);
        }
    }

    /// <returns>The weather to serve instead, or null to keep the one the server generated.</returns>
    public GetLocalWeatherResponseData? OnGenerated(string profileId, GetLocalWeatherResponseData generated)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return null;
        }

        lock (_gate)
        {
            if (_armed.Remove(profileId) && store.Read(profileId) is { Weather.Count: > 0 } kept)
            {
                return kept;
            }

            store.Write(profileId, generated);
            return null;
        }
    }

    public void Forget(string profileId)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return;
        }

        lock (_gate)
        {
            _armed.Remove(profileId);
            store.Delete(profileId);
        }
    }
}
