using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Services;

/// <summary>Removes from a raid's loot what the player already took. Pure: touches neither disk nor SPT services.</summary>
public static class LootFilter
{
    /// <param name="loot">Loot as generated at the start of the raid. Left untouched.</param>
    /// <param name="taken">Identifiers of the items the player took.</param>
    /// <param name="removed">Number of items removed, children included.</param>
    public static List<SpawnpointTemplate> WithoutTaken(IEnumerable<SpawnpointTemplate> loot, IReadOnlySet<string> taken, out int removed)
    {
        removed = 0;
        var result = new List<SpawnpointTemplate>();

        foreach (var spawnpoint in loot)
        {
            var items = spawnpoint.Items?.ToList();
            if (taken.Count == 0 || items is null || items.Count == 0)
            {
                result.Add(spawnpoint);
                continue;
            }

            var gone = GoneWithChildren(items, taken);
            if (gone.Count == 0)
            {
                result.Add(spawnpoint);
                continue;
            }

            removed += gone.Count;

            // Loose loot: the root is the item lying on the ground. Once taken, the whole point disappears.
            // Container: the root is the crate itself, which cannot be taken, so only its content shrinks.
            if (spawnpoint.Root is not null && gone.Contains(spawnpoint.Root))
            {
                continue;
            }

            result.Add(spawnpoint with { Items = items.Where(item => !gone.Contains(item.Id.ToString())).ToList() });
        }

        return result;
    }

    /// <summary>
    /// A taken item leaves with what it holds: the magazine of a weapon, the content of a backpack.
    /// Leaving the children behind would give the game items attached to a parent that no longer exists.
    /// </summary>
    private static HashSet<string> GoneWithChildren(List<SptLootItem> items, IReadOnlySet<string> taken)
    {
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var id = item.Id.ToString();
            if (taken.Contains(id))
            {
                gone.Add(id);
            }
        }

        if (gone.Count == 0)
        {
            return gone;
        }

        // Children can be listed before their parent: we loop until a pass finds nothing new
        bool grew;
        do
        {
            grew = false;
            foreach (var item in items)
            {
                if (item.ParentId is not null && gone.Contains(item.ParentId) && gone.Add(item.Id.ToString()))
                {
                    grew = true;
                }
            }
        } while (grew);

        return gone;
    }
}
