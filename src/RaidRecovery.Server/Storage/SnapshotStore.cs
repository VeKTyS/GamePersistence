using System.Text.Json;
using System.Text.RegularExpressions;
using RaidRecovery.Server.Models;

namespace RaidRecovery.Server.Storage;

/// <summary>
/// Writes and reads snapshots on disk, one per profile, rotating over two files:
/// &lt;profile&gt;.json (the latest) and &lt;profile&gt;.prev.json (the one before).
/// </summary>
public sealed partial class SnapshotStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public string Directory { get; } = directory;

    /// <summary>
    /// Writes the snapshot without ever leaving the current file in a partial state:
    /// we write next to it, force the physical write, then swap in one go.
    /// </summary>
    public void Write(string profileId, Snapshot snapshot)
    {
        var current = CurrentPath(profileId);
        var temp = TempPath(profileId);

        System.IO.Directory.CreateDirectory(Directory);

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, snapshot, JsonOptions);
            // Without this flush, the content can stay in the OS cache and be lost on a power cut
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(current))
        {
            // Atomic swap: temp becomes current, the old current becomes previous
            File.Replace(temp, current, PreviousPath(profileId), ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, current);
        }
    }

    /// <summary>
    /// Returns the last readable snapshot: the current one, otherwise the previous one if the current is missing or corrupt.
    /// </summary>
    public Snapshot? Read(string profileId)
    {
        foreach (var path in new[] { CurrentPath(profileId), PreviousPath(profileId) })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                var snapshot = JsonSerializer.Deserialize<Snapshot>(stream, JsonOptions);
                if (snapshot is not null)
                {
                    return snapshot;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Truncated or unreadable file: we try the next one
            }
        }

        return null;
    }

    /// <returns>true if at least one file was deleted.</returns>
    public bool Delete(string profileId)
    {
        var deleted = false;
        foreach (var path in new[] { CurrentPath(profileId), PreviousPath(profileId), TempPath(profileId) })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted = true;
            }
        }

        return deleted;
    }

    public static bool IsValidProfileId(string? profileId)
    {
        return profileId is not null && ProfileIdPattern().IsMatch(profileId);
    }

    private string CurrentPath(string profileId) => BuildPath(profileId, ".json");

    private string PreviousPath(string profileId) => BuildPath(profileId, ".prev.json");

    private string TempPath(string profileId) => BuildPath(profileId, ".json.tmp");

    private string BuildPath(string profileId, string suffix)
    {
        // The identifier ends up in a file path: we refuse anything that is not a profile id
        if (!IsValidProfileId(profileId))
        {
            throw new ArgumentException("Invalid profile identifier", nameof(profileId));
        }

        return Path.Combine(Directory, profileId + suffix);
    }

    [GeneratedRegex("^[a-fA-F0-9]{24}$")]
    private static partial Regex ProfileIdPattern();
}
