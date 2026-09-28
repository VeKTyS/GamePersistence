using RaidRecovery.Server.Storage;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Services;

/// <summary>What was done with the loot SPT just generated.</summary>
/// <param name="Replacement">Loot to serve instead, or null to keep the one SPT generated.</param>
/// <param name="Removed">Items already taken, removed from the replacement.</param>
/// <param name="Corpses">Bodies added to the replacement.</param>
/// <param name="SecondsLeft">Time left in the interrupted raid, if its bots are to be restored as well.</param>
/// <param name="BotsInSnapshot">Bots the snapshot stands for, alive or dead.</param>
public sealed record LootDecision(List<SpawnpointTemplate>? Replacement, int Removed, int Corpses = 0, int? SecondsLeft = null, int BotsInSnapshot = 0)
{
    /// <summary>true if the raid starts with the loot of the interrupted raid instead of new loot.</summary>
    public bool Replayed => Replacement is not null;
}

/// <summary>What a recovery hands over for the raid start that follows it.</summary>
/// <param name="InventoryIds">Everything the player carries: what comes from the map's loot is removed from it.</param>
/// <param name="Corpses">Bodies on the map, each as the JSON the game wrote. They replace those of the previous recovery.</param>
/// <param name="SecondsLeft">Set only when the snapshot holds the bots: the spawns already played are then dropped.</param>
/// <param name="BotsAlive">Bots alive in the snapshot. With the bodies, they are the bots the map must not spawn again.</param>
public sealed record RecoveryTicket(IEnumerable<string> InventoryIds, IReadOnlyList<string>? Corpses = null, int? SecondsLeft = null, int BotsAlive = 0);

/// <summary>
/// SPT draws new loot at every raid start. For a resumed raid to find its crates as they were left, we keep the
/// loot of the raid in progress and serve it again, minus what the player took, plus the bodies left behind.
/// </summary>
/// <param name="parseCorpse">Reads a body written by the game. null if it cannot be read.</param>
public sealed class LootReplayService(ILootStore store, Func<string, SpawnpointTemplate?>? parseCorpse = null)
{
    private readonly Lock _gate = new();

    // Profile -> raid about to be resumed. In memory only: the recovery and the raid start that
    // follows are a few seconds apart. If the server restarts in between, the raid gets new loot.
    private readonly Dictionary<string, (string Map, int? SecondsLeft, int Bots)> _armed = [];

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
            store.WriteNotes(profileId, new RecoveryNotes([.. taken], [.. ticket.Corpses ?? []], notes.Resumes + 1));
            _armed[profileId] = (map, ticket.SecondsLeft, ticket.BotsAlive + (ticket.Corpses?.Count ?? 0));
        }
    }

    /// <summary>How many times the raid in progress was resumed. Back to zero as soon as a new raid starts.</summary>
    public int ResumesDone(string profileId)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return 0;
        }

        lock (_gate)
        {
            return store.ReadNotes(profileId).Resumes;
        }
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
                    var notes = store.ReadNotes(profileId);
                    var taken = new HashSet<string>(notes.Taken, StringComparer.OrdinalIgnoreCase);
                    var replacement = LootFilter.WithoutTaken(stored.Loot, taken, out var removed);
                    var corpses = AddCorpses(replacement, notes.Corpses);
                    return new LootDecision(replacement, removed, corpses, armed.SecondsLeft, armed.Bots);
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

    /// <summary>A body that cannot be read is skipped: one bad body must not cost the whole loot.</summary>
    private int AddCorpses(List<SpawnpointTemplate> loot, List<string> corpses)
    {
        if (parseCorpse is null)
        {
            return 0;
        }

        var added = 0;
        foreach (var json in corpses)
        {
            SpawnpointTemplate? corpse;
            try
            {
                corpse = parseCorpse(json);
            }
            catch (Exception)
            {
                continue;
            }

            if (corpse?.Root is null || corpse.Items is null)
            {
                continue;
            }

            loot.Add(corpse);
            added++;
        }

        return added;
    }

    private static bool SameMap(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
