using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Storage;

/// <summary>Loot of the raid in progress, as the server generated it, with what the player took from it.</summary>
public sealed record StoredLoot(string Map, List<SpawnpointTemplate> Loot);

public interface ILootStore
{
    void WriteLoot(string profileId, StoredLoot loot);

    /// <summary>null if nothing is stored or if the file is unreadable.</summary>
    StoredLoot? ReadLoot(string profileId);

    void WriteTaken(string profileId, IReadOnlyCollection<string> ids);

    IReadOnlyCollection<string> ReadTaken(string profileId);

    /// <summary>Deletes only the list of taken items: a new raid starts with loot nobody touched.</summary>
    void DeleteTaken(string profileId);

    void Delete(string profileId);
}
