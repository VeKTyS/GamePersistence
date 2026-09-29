using RaidRecovery.Server.Storage;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Services;

/// <summary>What was done with the loot SPT just generated.</summary>
/// <param name="Replacement">Loot to serve instead, or null to keep the one SPT generated.</param>
/// <param name="Removed">Items that left the map, removed from the replacement.</param>
/// <param name="Corpses">Bodies added to the replacement.</param>
/// <param name="SecondsPlayed">Time played in the raid so far, all its resumes added up, if its bots are to be restored as well.</param>
/// <param name="BotsInSnapshot">Bots the snapshot stands for, alive or dead.</param>
/// <param name="Loose">Items dropped or moved by the player, added to the replacement.</param>
public sealed record LootDecision(
    List<SpawnpointTemplate>? Replacement,
    int Removed,
    int Corpses = 0,
    int? SecondsPlayed = null,
    int BotsInSnapshot = 0,
    int Loose = 0
)
{
    /// <summary>true if the raid starts with the loot of the interrupted raid instead of new loot.</summary>
    public bool Replayed => Replacement is not null;
}

/// <summary>What a recovery hands over for the raid start that follows it.</summary>
/// <param name="InventoryIds">Everything the player carries: what comes from the map's loot is removed from it.</param>
/// <param name="Corpses">Bodies on the map, each as the JSON the game wrote. They replace those of the previous recovery.</param>
/// <param name="SecondsPlayed">Time played since the raid, or its last resume, started. Set only when the snapshot holds the bots.</param>
/// <param name="BotsAlive">Bots alive in the snapshot. With the bodies, they are the bots the map must not spawn again.</param>
/// <param name="Gone">Loot items the game saw on the map and no longer sees. They count as taken, whoever holds them.</param>
/// <param name="Loose">Items lying where the map did not put them, each as the JSON the game wrote.</param>
/// <param name="SnapshotId">Tells one snapshot from another. null: every recovery counts as a new one.</param>
public sealed record RecoveryTicket(
    IEnumerable<string> InventoryIds,
    IReadOnlyList<string>? Corpses = null,
    int? SecondsPlayed = null,
    int BotsAlive = 0,
    IReadOnlyCollection<string>? Gone = null,
    IReadOnlyList<string>? Loose = null,
    string? SnapshotId = null
);

/// <summary>
/// SPT draws new loot at every raid start. For a resumed raid to find its crates as they were left, we keep the
/// loot of the raid in progress and serve it again, minus what left the map, plus what was left on it.
/// </summary>
/// <param name="parseLoot">Reads a loot entry written by the game, body or item. null if it cannot be read.</param>
public sealed class LootReplayService(ILootStore store, Func<string, SpawnpointTemplate?>? parseLoot = null)
{
    private readonly Lock _gate = new();

    // Profile -> raid about to be resumed. In memory only: the recovery and the raid start that
    // follows are a few seconds apart. If the server restarts in between, the raid gets new loot.
    private readonly Dictionary<string, (string Map, int? SecondsPlayed, int Bots)> _armed = [];

    /// <summary>
    /// A recovery was just applied: the next raid start on this map is a resume.
    /// The taken items add up from one recovery to the next, so an item taken then used up does not come back.
    /// </summary>
    public void Arm(string profileId, string map, RecoveryTicket ticket)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return;
        }

        lock (_gate)
        {
            var notes = store.ReadNotes(profileId);
            var taken = new HashSet<string>(notes.Taken, StringComparer.OrdinalIgnoreCase);
            taken.UnionWith(ticket.InventoryIds);
            taken.UnionWith(ticket.Gone ?? []);

            // The game crashed while the resumed raid was loading: the same snapshot is played again.
            // Its time and its recovery are already counted, adding them again would count them twice.
            var again = IsSameSnapshot(notes, ticket.SnapshotId);

            // The clock of a resumed raid restarts at zero: the time played adds up from one resume to the next
            int? played = ticket.SecondsPlayed is { } seconds ? notes.SecondsPlayed + (again ? 0 : Math.Max(0, seconds)) : null;
            store.WriteNotes(
                profileId,
                new RecoveryNotes([.. taken], [.. ticket.Corpses ?? []], notes.Resumes + (again ? 0 : 1))
                {
                    Loose = MergeLoose(notes.Loose, ticket.Loose ?? [], taken),
                    SecondsPlayed = played ?? notes.SecondsPlayed,
                    LastSnapshot = ticket.SnapshotId,
                }
            );
            _armed[profileId] = (map, played, ticket.BotsAlive + (ticket.Corpses?.Count ?? 0));
        }
    }

    /// <summary>
    /// How many times the raid in progress was resumed before the recovery of this snapshot. Back to zero as
    /// soon as a new raid starts. A snapshot played again does not count its own first recovery.
    /// </summary>
    public int ResumesDone(string profileId, string? snapshotId = null)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return 0;
        }

        lock (_gate)
        {
            var notes = store.ReadNotes(profileId);
            return IsSameSnapshot(notes, snapshotId) ? Math.Max(0, notes.Resumes - 1) : notes.Resumes;
        }
    }

    private static bool IsSameSnapshot(RecoveryNotes notes, string? snapshotId)
    {
        return snapshotId is not null && string.Equals(notes.LastSnapshot, snapshotId, StringComparison.Ordinal);
    }

    /// <summary>Called each time SPT generates the loot of a raid.</summary>
    public LootDecision OnLootGenerated(string profileId, string map, List<SpawnpointTemplate> generated)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return new LootDecision(null, 0);
        }

        lock (_gate)
        {
            var resuming = _armed.Remove(profileId, out var armed) && SameMap(armed.Map, map);
            if (resuming)
            {
                var stored = store.ReadLoot(profileId);
                if (stored is not null && SameMap(stored.Map, map))
                {
                    return Replay(stored, store.ReadNotes(profileId), armed.SecondsPlayed, armed.Bots);
                }
            }

            // New raid, or nothing usable to replay: this loot becomes the reference
            store.DeleteNotes(profileId);
            store.WriteLoot(profileId, new StoredLoot(map, generated));
            return new LootDecision(null, 0);
        }
    }

    /// <summary>The raid ended normally or was discarded: its loot will never be served again.</summary>
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

    private LootDecision Replay(StoredLoot stored, RecoveryNotes notes, int? secondsPlayed, int bots)
    {
        var taken = new HashSet<string>(notes.Taken, StringComparer.OrdinalIgnoreCase);
        var loose = Parse(notes.Loose).Where(entry => !taken.Contains(entry.Root!)).ToList();
        var moved = new HashSet<string>(loose.Select(entry => entry.Root!), StringComparer.OrdinalIgnoreCase);
        // An item the player moved is served where they left it, not also where the map had put it
        var original = stored.Loot.Where(spawnpoint => spawnpoint.Root is null || !moved.Contains(spawnpoint.Root)).ToList();

        var replacement = LootFilter.WithoutTaken(original, taken, out var removed);
        replacement.AddRange(loose);
        var corpses = Parse(notes.Corpses);
        replacement.AddRange(corpses);
        return new LootDecision(replacement, removed, corpses.Count, secondsPlayed, bots, loose.Count);
    }

    /// <summary>
    /// Items dropped before an earlier recovery are part of the loot of the resumed raid, so the game no longer
    /// reports them as dropped. They are kept until they leave the map, unless a newer entry moves them.
    /// </summary>
    private List<string> MergeLoose(List<string> before, IReadOnlyList<string> now, HashSet<string> taken)
    {
        var merged = new List<string>(now);
        var replaced = new HashSet<string>(Parse(now).Select(entry => entry.Root!), StringComparer.OrdinalIgnoreCase);
        foreach (var json in before)
        {
            var entry = Parse([json]).FirstOrDefault();
            if (entry is not null && !taken.Contains(entry.Root!) && !replaced.Contains(entry.Root!))
            {
                merged.Add(json);
            }
        }

        return merged;
    }

    /// <summary>An entry that cannot be read is skipped: one bad entry must not cost the whole loot.</summary>
    private List<SpawnpointTemplate> Parse(IEnumerable<string> entries)
    {
        var parsed = new List<SpawnpointTemplate>();
        if (parseLoot is null)
        {
            return parsed;
        }

        foreach (var json in entries)
        {
            SpawnpointTemplate? entry;
            try
            {
                entry = parseLoot(json);
            }
            catch (Exception)
            {
                continue;
            }

            if (entry?.Root is not null && entry.Items is not null)
            {
                parsed.Add(entry);
            }
        }

        return parsed;
    }

    private static bool SameMap(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
