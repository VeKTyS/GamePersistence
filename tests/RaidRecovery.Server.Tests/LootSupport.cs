using RaidRecovery.Server.Storage;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Tests;

/// <summary>Store kept in memory: the rules are tested without disk and without SPT's serializer.</summary>
public sealed class MemoryLootStore : ILootStore
{
    private readonly Dictionary<string, StoredLoot> _loot = [];
    private readonly Dictionary<string, RecoveryNotes> _notes = [];

    public int LootWrites { get; private set; }

    public void WriteLoot(string profileId, StoredLoot loot)
    {
        LootWrites++;
        _loot[profileId] = loot;
    }

    public StoredLoot? ReadLoot(string profileId) => _loot.GetValueOrDefault(profileId);

    public void WriteNotes(string profileId, RecoveryNotes notes) => _notes[profileId] = notes;

    public RecoveryNotes ReadNotes(string profileId) => _notes.GetValueOrDefault(profileId) ?? RecoveryNotes.Empty;

    public void DeleteNotes(string profileId) => _notes.Remove(profileId);

    public void Delete(string profileId)
    {
        _loot.Remove(profileId);
        _notes.Remove(profileId);
    }
}

public static class Loot
{
    public const string Crate = "c00000000000000000000001";
    public const string Bandage = "c00000000000000000000002";
    public const string Bolts = "c00000000000000000000003";
    public const string Rifle = "d00000000000000000000001";
    public const string Magazine = "d00000000000000000000002";
    public const string Round = "d00000000000000000000003";
    public const string Wrench = "e00000000000000000000001";

    private const string AnyTemplate = "544fb25a4bdc2dfb738b4567";

    public static SptLootItem Item(string id, string? parentId = null)
    {
        return new SptLootItem
        {
            Id = new MongoId(id),
            Template = new MongoId(AnyTemplate),
            ParentId = parentId,
        };
    }

    /// <summary>A crate holding two items, a rifle on the ground with its loaded magazine, a wrench on the ground.</summary>
    public static List<SpawnpointTemplate> Sample()
    {
        return
        [
            new SpawnpointTemplate
            {
                Id = "crate",
                IsContainer = true,
                Root = Crate,
                Items = [Item(Crate), Item(Bandage, Crate), Item(Bolts, Crate)],
            },
            new SpawnpointTemplate
            {
                Id = "rifle",
                IsContainer = false,
                Root = Rifle,
                // The round is listed before the magazine that holds it, on purpose
                Items = [Item(Rifle), Item(Round, Magazine), Item(Magazine, Rifle)],
            },
            new SpawnpointTemplate
            {
                Id = "wrench",
                IsContainer = false,
                Root = Wrench,
                Items = [Item(Wrench)],
            },
        ];
    }

    public static List<string> Ids(IEnumerable<SpawnpointTemplate> loot)
    {
        return loot.SelectMany(spawnpoint => spawnpoint.Items ?? []).Select(item => item.Id.ToString()).ToList();
    }
}
