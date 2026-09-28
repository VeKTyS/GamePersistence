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
        return Body(
            new PendingResponse
            {
                Pending = true,
                Map = snapshot.Map,
                SecondsLeft = snapshot.Raid?.SecondsLeft,
                SavedAt = snapshot.SavedAt,
                Restorable = ProfileRestorer.IsRestorable(snapshot),
            }
        );
    }

    public async ValueTask<string> Restore(MongoId sessionId, CancellationToken cancellationToken)
    {
        var result = await host.Service.RestoreAsync(
            sessionId.ToString(),
            snapshot => restorer.ApplyAsync(sessionId, snapshot, cancellationToken)
        );

        if (!result.Restored)
        {
            logger.Warning($"[RaidRecovery] Recovery failed: {result.Failure}");
            return httpResponseUtil.GetBody(new RestoreResponse { Restored = false, Reason = result.Failure });
        }

        var snapshot = result.Snapshot!;
        ArmLootReplay(sessionId, snapshot);
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
            }
        );
    }

    public ValueTask<string> Discard(MongoId sessionId)
    {
        var discarded = host.Service.Discard(sessionId.ToString());
        host.Loot.Forget(sessionId.ToString());
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
                new RecoveryTicket(InventoryIds(snapshot), Corpses(snapshot), holdsBots ? snapshot.Raid?.SecondsLeft : null)
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
        var corpses = new List<string>();
        if (
            snapshot.World is not { ValueKind: JsonValueKind.Object } world
            || !world.TryGetProperty("corpses", out var list)
            || list.ValueKind != JsonValueKind.Array
        )
        {
            return corpses;
        }

        foreach (var corpse in list.EnumerateArray())
        {
            if (corpse.ValueKind == JsonValueKind.Object)
            {
                corpses.Add(corpse.GetRawText());
            }
        }

        return corpses;
    }

    private ValueTask<string> Body<T>(T data)
    {
        return new ValueTask<string>(httpResponseUtil.GetBody(data));
    }
}
