using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Storage;

/// <summary>Loot of the raid in progress, as the server generated it.</summary>
public sealed record StoredLoot(string Map, List<SpawnpointTemplate> Loot);

/// <summary>What the recoveries of a raid taught us about its loot.</summary>
/// <param name="Taken">Identifiers of the items the player took. They add up from one recovery to the next.</param>
/// <param name="Corpses">Bodies lying on the map at the last snapshot, each as the JSON the game wrote.</param>
/// <param name="Resumes">How many times this raid was resumed so far.</param>
public sealed record RecoveryNotes(List<string> Taken, List<string> Corpses, int Resumes = 0)
{
    /// <summary>Time played in the raid so far, all its resumes added up.</summary>
    public int SecondsPlayed { get; init; }

    /// <summary>Items lying where the map did not put them, each as the JSON the game wrote.</summary>
    public List<string> Loose { get; init; } = [];

    /// <summary>Snapshot the last recovery came from. The same one played again is not a new recovery.</summary>
    public string? LastSnapshot { get; init; }

    public static RecoveryNotes Empty => new([], []);
}

public interface ILootStore
{
    void WriteLoot(string profileId, StoredLoot loot);

    /// <summary>null if nothing is stored or if the file is unreadable.</summary>
    StoredLoot? ReadLoot(string profileId);

    void WriteNotes(string profileId, RecoveryNotes notes);

    /// <summary>Empty notes if nothing is stored or if the file is unreadable.</summary>
    RecoveryNotes ReadNotes(string profileId);

    /// <summary>Deletes only the notes: a new raid starts with loot nobody touched.</summary>
    void DeleteNotes(string profileId);

    void Delete(string profileId);
}
