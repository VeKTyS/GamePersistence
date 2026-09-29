using System.Text.Json;
using RaidRecovery.Server.Models;
using RaidRecovery.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace RaidRecovery.Server.Callbacks;

[Injectable]
public class RaidRecoveryCallbacks(
    ISptLogger<RaidRecoveryCallbacks> logger,
    HttpResponseUtil httpResponseUtil,
    RaidRecoveryHost host,
    ProfileRestorer restorer
)
{
    public ValueTask<string> Save(Snapshot snapshot, MongoId sessionId)
    {
        var outcome = host.Service.Save(sessionId.ToString(), snapshot);
        if (outcome != SaveOutcome.Saved)
        {
            logger.Warning($"[RaidRecovery] Snapshot refused: {outcome}");
        }

        return Body(new SaveResponse { Saved = outcome == SaveOutcome.Saved, Reason = outcome == SaveOutcome.Saved ? null : outcome.ToString() });
    }

    public ValueTask<string> Pending(MongoId sessionId)
    {
        var snapshot = host.Service.GetPending(sessionId.ToString());
        if (snapshot is null)
        {
            return Body(new PendingResponse { Pending = false });
        }

        logger.Info($"[RaidRecovery] Interrupted raid detected on {snapshot.Map}, snapshot from {snapshot.SavedAt:u}");
        var refusal = Refusal(sessionId, snapshot);
        if (refusal is not null)
        {
            logger.Warning($"[RaidRecovery] This raid cannot be resumed: {refusal}");
        }

        return Body(
            new PendingResponse
            {
                Pending = true,
                Map = snapshot.Map,
                SecondsLeft = snapshot.Raid?.SecondsLeft,
                SavedAt = snapshot.SavedAt,
                Restorable = ProfileRestorer.IsRestorable(snapshot) && refusal is null,
                Side = snapshot.Raid?.Side,
                Reason = refusal,
            }
        );
    }

    public async ValueTask<string> Restore(MongoId sessionId, CancellationToken cancellationToken)
    {
        // Checked again here: the window of the game is not the only way to reach this route
        var result = await host.Service.RestoreAsync(
            sessionId.ToString(),
            snapshot =>
                Refusal(sessionId, snapshot) is { } refusal
                    ? Task.FromResult<string?>(refusal)
                    : restorer.ApplyAsync(sessionId, snapshot, cancellationToken)
        );

        if (!result.Restored)
        {
            logger.Warning($"[RaidRecovery] Recovery failed: {result.Failure}");
            return httpResponseUtil.GetBody(new RestoreResponse { Restored = false, Reason = result.Failure });
        }

        var snapshot = result.Snapshot!;
        ArmLootReplay(sessionId, snapshot);
        host.Weather.Arm(sessionId.ToString());
        logger.Success($"[RaidRecovery] Inventory and health restored, resuming on {snapshot.Map}");
        return httpResponseUtil.GetBody(
            new RestoreResponse
            {
                Restored = true,
                Map = snapshot.Map,
                DateTime = snapshot.Raid?.DateTime,
                SecondsLeft = snapshot.Raid?.SecondsLeft,
                Position = snapshot.Player?.Position,
                Rotation = snapshot.Player?.Rotation,
                World = snapshot.World,
                Bots = snapshot.Bots,
                Stats = snapshot.Player?.Stats,
                Side = snapshot.Raid?.Side,
                Stance = snapshot.Player?.Stance,
            }
        );
    }

    private string? Refusal(MongoId sessionId, Snapshot snapshot)
    {
        return ResumePolicy.Refusal(snapshot, host.Loot.ResumesDone(sessionId.ToString()), host.Config);
    }

    public ValueTask<string> Discard(MongoId sessionId)
    {
        var discarded = host.Service.Discard(sessionId.ToString());
        host.Loot.Forget(sessionId.ToString());
        host.Weather.Forget(sessionId.ToString());
        if (discarded)
        {
            logger.Info("[RaidRecovery] Snapshot discarded");
        }

        return Body(new DiscardResponse { Discarded = discarded });
    }

    /// <summary>
    /// Called before SPT handles /client/match/local/start. Must never throw:
    /// an exception here would prevent SPT from starting the raid.
    /// </summary>
    public string RaidStarted(MongoId sessionId, string? output)
    {
        try
        {
            if (host.Service.OnRaidStarted(sessionId.ToString()))
            {
                logger.Info("[RaidRecovery] Snapshot from a previous raid purged at the start of the new raid");
            }
        }
        catch (Exception ex)
        {
            logger.Error("[RaidRecovery] Failure at raid start, SPT carries on normally", ex);
        }

        return output ?? string.Empty;
    }

    /// <summary>
    /// Called before SPT handles /client/match/local/end. Must never throw:
    /// an exception here would prevent SPT from saving the raid result.
    /// </summary>
    public string RaidEnded(MongoId sessionId, string? output)
    {
        try
        {
            if (host.Service.OnRaidEnded(sessionId.ToString()))
            {
                logger.Info("[RaidRecovery] Raid end: snapshot purged");
            }

            host.Loot.Forget(sessionId.ToString());
            host.Weather.Forget(sessionId.ToString());
        }
        catch (Exception ex)
        {
            logger.Error("[RaidRecovery] Purge at raid end failed, SPT carries on normally", ex);
        }

        return output ?? string.Empty;
    }

    /// <summary>
    /// Everything the player carries at the time of the snapshot counts as taken. Most of it never was in the
    /// raid's loot: those identifiers simply match nothing.
    /// </summary>
    private void ArmLootReplay(MongoId sessionId, Snapshot snapshot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(snapshot.Map))
            {
                return;
            }

            // The spawns already played are only dropped when the snapshot holds the bots that came out of them
            var holdsBots = snapshot.Bots is { ValueKind: JsonValueKind.Array };
            host.Loot.Arm(
                sessionId.ToString(),
                snapshot.Map,
                new RecoveryTicket(
                    InventoryIds(snapshot),
                    Corpses(snapshot),
                    holdsBots ? snapshot.Raid?.SecondsPlayed : null,
                    holdsBots ? snapshot.Bots!.Value.GetArrayLength() : 0,
                    Gone(snapshot),
                    RawEntries(snapshot, "loose")
                )
            );
        }
        catch (Exception ex)
        {
            // The profile is already restored: the raid resumes, with new loot
            logger.Error("[RaidRecovery] Loot replay could not be prepared", ex);
        }
    }

    internal static List<string> InventoryIds(Snapshot snapshot)
    {
        var ids = new List<string>();
        if (
            snapshot.Player?.Profile is not { ValueKind: JsonValueKind.Object } profile
            || !profile.TryGetProperty("Inventory", out var inventory)
            || inventory.ValueKind != JsonValueKind.Object
            || !inventory.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array
        )
        {
            return ids;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("_id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                ids.Add(id.GetString()!);
            }
        }

        return ids;
    }

    /// <summary>Bodies on the map, each kept as the text the game wrote: the server does not interpret them.</summary>
    internal static List<string> Corpses(Snapshot snapshot)
    {
        return RawEntries(snapshot, "corpses");
    }

    /// <summary>Loot entries of the map state, each kept as the text the game wrote.</summary>
    internal static List<string> RawEntries(Snapshot snapshot, string name)
    {
        var entries = new List<string>();
        if (
            snapshot.World is not { ValueKind: JsonValueKind.Object } world
            || !world.TryGetProperty(name, out var list)
            || list.ValueKind != JsonValueKind.Array
        )
        {
            return entries;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object)
            {
                entries.Add(entry.GetRawText());
            }
        }

        return entries;
    }

    /// <summary>Loot items that left the map. Empty when the snapshot does not say: an older plugin, or a reading that failed.</summary>
    internal static List<string> Gone(Snapshot snapshot)
    {
        if (
            snapshot.World is not { ValueKind: JsonValueKind.Object } world
            || !world.TryGetProperty("gone", out var list)
            || list.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        return list.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String).Select(id => id.GetString()!).ToList();
    }

    private ValueTask<string> Body<T>(T data)
    {
        return new ValueTask<string>(httpResponseUtil.GetBody(data));
    }
}
