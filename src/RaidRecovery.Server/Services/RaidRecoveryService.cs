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
    /// Plays the recovery: applies the snapshot, then deletes it. The order matters. If applying fails,
    /// the snapshot stays available for another try; if it succeeds, it can no longer be replayed.
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
                store.Delete(profileId);
            }

            return new RestoreResult(snapshot, null);
        }
        finally
        {
            _restoreGate.Release();
        }
    }

    /// <summary>Deliberate discard or death in raid: we delete and close the raid to late sends.</summary>
    public bool Discard(string profileId)
    {
        return CloseRaid(profileId);
    }

    /// <summary>A snapshot still there when a raid starts is an abandoned raid: it must not come back.</summary>
    public bool OnRaidStarted(string profileId)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return false;
        }

        lock (_gate)
        {
            _raidOpen[profileId] = true;
            return store.Delete(profileId);
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
