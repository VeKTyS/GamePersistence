using RaidRecovery.Server.Storage;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Services;

/// <summary>What was done with the loot SPT just generated.</summary>
public sealed record LootDecision(List<SpawnpointTemplate>? Replacement, int Removed)
{
    /// <summary>true if the raid starts with the loot of the interrupted raid instead of new loot.</summary>
    public bool Replayed => Replacement is not null;
}

/// <summary>
/// SPT draws new loot at every raid start. For a resumed raid to find its crates as they were left, we keep the
/// loot of the raid in progress and serve it again, minus what the player took.
/// </summary>
public sealed class LootReplayService(ILootStore store)
{
    private readonly Lock _gate = new();

    // Profile -> map of the raid about to be resumed. In memory only: the recovery and the raid start that
    // follows are a few seconds apart. If the server restarts in between, the raid gets new loot.
    private readonly Dictionary<string, string> _armed = [];

    /// <summary>
    /// A recovery was just applied: the next raid start on this map is a resume.
    /// The taken items add up from one recovery to the next, so an item taken then used up does not come back.
    /// </summary>
    public void Arm(string profileId, string map, IEnumerable<string> inventoryIds)
    {
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            return;
        }

        lock (_gate)
        {
            var taken = new HashSet<string>(store.ReadTaken(profileId), StringComparer.OrdinalIgnoreCase);
            taken.UnionWith(inventoryIds);
            store.WriteTaken(profileId, taken);
            _armed[profileId] = map;
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
            var resuming = _armed.Remove(profileId, out var armedMap) && SameMap(armedMap, map);
            if (resuming)
            {
                var stored = store.ReadLoot(profileId);
                if (stored is not null && SameMap(stored.Map, map))
                {
                    var taken = new HashSet<string>(store.ReadTaken(profileId), StringComparer.OrdinalIgnoreCase);
                    var replacement = LootFilter.WithoutTaken(stored.Loot, taken, out var removed);
                    return new LootDecision(replacement, removed);
                }
            }

            // New raid, or nothing usable to replay: this loot becomes the reference
            store.DeleteTaken(profileId);
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

    private static bool SameMap(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
