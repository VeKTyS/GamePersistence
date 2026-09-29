using RaidRecovery.Server.Models;
using RaidRecovery.Server.Storage;

namespace RaidRecovery.Server.Services;

/// <summary>Outcome of a recovery: the snapshot applied, or the reason why nothing was done.</summary>
public record RestoreResult(Snapshot? Snapshot, string? Failure)
{
    public bool Restored => Snapshot is not null;
}

public enum SaveOutcome
{
    Saved,
    InvalidProfile,
    InvalidSnapshot,
    UnsupportedVersion,
    RaidClosed,
}

/// <summary>
/// Lifecycle rules of a snapshot. Depends neither on SPT nor on the system clock, to stay testable.
/// </summary>
public sealed class RaidRecoveryService(SnapshotStore store, TimeProvider clock, TimeSpan maxAge)
{
    // A single lock for everything: a late send must not be able to slip in between
    // "the raid is closed" and "the file is deleted".
    private readonly Lock _gate = new();

    private readonly SemaphoreSlim _restoreGate = new(1, 1);

    // true = raid in progress, false = raid over. Absent = unknown (server restarted in the middle of a raid).
    private readonly Dictionary<string, bool> _raidOpen = [];

    // Profiles whose snapshot was applied and whose raid has not started again yet. In memory only: if the
    // server restarts in between, the next raid start takes the snapshot for a leftover and purges it.
    private readonly HashSet<string> _resuming = [];

    // When each player last resumed a raid. In memory only: after a restart of the server, those who
    // played with them are told the host is not back, and wait.
    private readonly Dictionary<string, DateTimeOffset> _lastResume = [];

    public SaveOutcome Save(string profileId, Snapshot? snapshot)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return SaveOutcome.InvalidProfile;
        }

        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.SessionId) || string.IsNullOrWhiteSpace(snapshot.Map))
        {
            return SaveOutcome.InvalidSnapshot;
        }

        if (snapshot.Version < 1 || snapshot.Version > Snapshot.CurrentVersion)
        {
            return SaveOutcome.UnsupportedVersion;
        }

        lock (_gate)
        {
            // A send that left just before the end of the raid can arrive after the purge: we refuse it,
            // otherwise it would recreate a snapshot for a raid that ended normally.
            if (_raidOpen.TryGetValue(profileId, out var open) && !open)
            {
                return SaveOutcome.RaidClosed;
            }

            // The profile comes from the session and the date from the server clock: we do not write what the client claims
            store.Write(profileId, snapshot with { ProfileId = profileId, SavedAt = clock.GetUtcNow() });
            return SaveOutcome.Saved;
        }
    }

    /// <summary>Snapshot waiting to be resumed, or null. Deletes what is expired or unreadable along the way.</summary>
    public Snapshot? GetPending(string profileId)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return null;
        }

        lock (_gate)
        {
            var snapshot = store.Read(profileId);
            if (snapshot is null)
            {
                // Nothing readable: if files remain, they are corrupt and will never be of use
                store.Delete(profileId);
                return null;
            }

            if (!IsUsable(snapshot))
            {
                store.Delete(profileId);
                return null;
            }

            return snapshot;
        }
    }

    /// <summary>
    /// Plays the recovery: applies the snapshot and keeps it. A raid is over when the player extracts, dies
    /// or leaves it, not when it is resumed: if the game crashes while the resumed raid loads, the same
    /// snapshot is offered again. Applying it twice gives the same profile as applying it once.
    /// </summary>
    /// <param name="apply">Applies the snapshot to the profile. Returns null if all went well, otherwise the reason for the failure.</param>
    public async Task<RestoreResult> RestoreAsync(string profileId, Func<Snapshot, Task<string?>> apply)
    {
        // Two simultaneous recovery requests must not apply the same snapshot in parallel
        await _restoreGate.WaitAsync();
        try
        {
            var snapshot = GetPending(profileId);
            if (snapshot is null)
            {
                return new RestoreResult(null, "NoSnapshot");
            }

            var failure = await apply(snapshot);
            if (failure is not null)
            {
                return new RestoreResult(null, failure);
            }

            lock (_gate)
            {
                _resuming.Add(profileId);
                _lastResume[profileId] = clock.GetUtcNow();
            }

            return new RestoreResult(snapshot, null);
        }
        finally
        {
            _restoreGate.Release();
        }
    }

    /// <summary>When this player last resumed a raid, or null. Asked by the players who were in it.</summary>
    public DateTimeOffset? LastResume(string? profileId)
    {
        if (profileId is null || !SnapshotStore.IsValidProfileId(profileId))
        {
            return null;
        }

        lock (_gate)
        {
            return _lastResume.TryGetValue(profileId, out var at) ? at : null;
        }
    }

    /// <summary>Deliberate discard or death in raid: we delete and close the raid to late sends.</summary>
    public bool Discard(string profileId)
    {
        return CloseRaid(profileId);
    }

    /// <summary>
    /// A snapshot still there when a raid starts is an abandoned raid: it must not come back. Unless that
    /// raid is the resumed one: its snapshot stays until the first snapshot of the resumed raid replaces it.
    /// </summary>
    /// <returns>true if a leftover snapshot was purged.</returns>
    public bool OnRaidStarted(string profileId)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return false;
        }

        lock (_gate)
        {
            _raidOpen[profileId] = true;
            return !_resuming.Remove(profileId) && store.Delete(profileId);
        }
    }

    /// <summary>
    /// The map of the raid that starts is known a moment after its start. A snapshot taken on another map
    /// belongs to a raid the player gave up by starting this one.
    /// </summary>
    /// <returns>true if a snapshot of another map was purged.</returns>
    public bool OnRaidMapKnown(string profileId, string? map)
    {
        if (!SnapshotStore.IsValidProfileId(profileId) || string.IsNullOrWhiteSpace(map))
        {
            return false;
        }

        lock (_gate)
        {
            var snapshot = store.Read(profileId);
            return snapshot is not null && !string.Equals(snapshot.Map, map, StringComparison.OrdinalIgnoreCase) && store.Delete(profileId);
        }
    }

    public bool OnRaidEnded(string profileId)
    {
        return CloseRaid(profileId);
    }

    private bool CloseRaid(string profileId)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return false;
        }

        lock (_gate)
        {
            _raidOpen[profileId] = false;
            _resuming.Remove(profileId);
            return store.Delete(profileId);
        }
    }

    private bool IsUsable(Snapshot snapshot)
    {
        if (snapshot.Version < 1 || snapshot.Version > Snapshot.CurrentVersion)
        {
            return false;
        }

        if (snapshot.SavedAt is not { } savedAt)
        {
            return false;
        }

        return clock.GetUtcNow() - savedAt <= maxAge;
    }
}
