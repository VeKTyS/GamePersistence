using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Storage;

/// <summary>
/// Two files per profile next to the snapshot: &lt;profile&gt;.loot.json, written once per raid,
/// and &lt;profile&gt;.notes.json, rewritten at each recovery.
/// The loot goes through SPT's serializer: it is the one that knows how to write and read its own types.
/// </summary>
public sealed class LootStore(string directory, Func<StoredLoot, string?> serialize, Func<string, StoredLoot?> deserialize) : ILootStore
{
    public void WriteLoot(string profileId, StoredLoot loot)
    {
        var json = serialize(loot) ?? throw new InvalidOperationException("The loot could not be serialized");
        WriteAtomically(LootPath(profileId), json);
    }

    public StoredLoot? ReadLoot(string profileId)
    {
        var path = LootPath(profileId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var loot = deserialize(File.ReadAllText(path));
            return loot?.Map is null || loot.Loot is null ? null : loot;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public void WriteNotes(string profileId, RecoveryNotes notes)
    {
        WriteAtomically(NotesPath(profileId), JsonSerializer.Serialize(notes));
    }

    public RecoveryNotes ReadNotes(string profileId)
    {
        var path = NotesPath(profileId);
        if (!File.Exists(path))
        {
            return RecoveryNotes.Empty;
        }

        try
        {
            var notes = JsonSerializer.Deserialize<RecoveryNotes>(File.ReadAllText(path));
            return notes?.Taken is null || notes.Corpses is null ? RecoveryNotes.Empty : notes;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return RecoveryNotes.Empty;
        }
    }

    public void DeleteNotes(string profileId)
    {
        File.Delete(NotesPath(profileId));
    }

    public void Delete(string profileId)
    {
        File.Delete(LootPath(profileId));
        File.Delete(NotesPath(profileId));
    }

    private void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        // A crash in the middle of the write leaves the previous file intact
        File.Move(temp, path, overwrite: true);
    }

    private string LootPath(string profileId) => BuildPath(profileId, ".loot.json");

    private string NotesPath(string profileId) => BuildPath(profileId, ".notes.json");

    private string BuildPath(string profileId, string suffix)
    {
        // The identifier ends up in a file path: we refuse anything that is not a profile id
        if (!SnapshotStore.IsValidProfileId(profileId))
        {
            throw new ArgumentException("Invalid profile identifier", nameof(profileId));
        }

        return Path.Combine(directory, profileId + suffix);
    }
}
