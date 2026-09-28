using System.Reflection;
using RaidRecovery.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.InRaid;

namespace RaidRecovery.Server.Patches;

/// <summary>
/// Runs right after SPT generated the map and its loot for a raid. It is the only place where we have at once
/// the profile, the map and the loot, before the response leaves for the game.
/// </summary>
public class LootGeneratedPatch : AbstractPatch
{
    // A patch is a static method: SPT cannot inject anything into it, the host hands these over at load time
    internal static LootReplayService? Service { get; set; }

    internal static ISptLogger<RaidRecoveryHost>? Logger { get; set; }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.GenerateLocationAndLoot))
            ?? throw new MissingMethodException(nameof(LocationLifecycleService), nameof(LocationLifecycleService.GenerateLocationAndLoot));
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, string name, LocationBase __result)
    {
        // Must never throw: an exception here would prevent SPT from starting the raid
        try
        {
            // No loot: hideout, or generation skipped at the request of the game
            if (Service is null || __result?.Loot is null)
            {
                return;
            }

            var decision = Service.OnLootGenerated(sessionId.ToString(), name, __result.Loot.ToList());
            if (!decision.Replayed)
            {
                return;
            }

            __result.Loot = decision.Replacement!;
            Logger?.Success(
                $"[RaidRecovery] Loot of the interrupted raid served again on {name}: {decision.Replacement!.Count} spawn points, {decision.Removed} items that left the map removed, {decision.Loose} dropped items and {decision.Corpses} bodies put back"
            );

            // The time played comes from the clock of the game, not from the length of the raid: that length
            // is drawn again at each start for a scav, and a subtraction on it gave a negative time played
            if (decision.SecondsPlayed is { } elapsed)
            {
                var shift = WaveShift.Apply(__result, elapsed, decision.BotsInSnapshot);
                Logger?.Success(
                    $"[RaidRecovery] Bot spawns after {elapsed} s played and {decision.BotsInSnapshot} bots in the snapshot: {shift.Removed} removed, {shift.Replayed} played again at once, {shift.Shifted} still to come"
                );
            }
        }
        catch (Exception ex)
        {
            Logger?.Error("[RaidRecovery] Loot replay failed, the raid starts with new loot", ex);
        }
    }
}
