using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Counters;
using HarmonyLib;
using RaidRecovery.Client.Raid;
using RaidRecovery.Client.Recovery;
using SPT.Reflection.Patching;

namespace RaidRecovery.Client.Patches
{
    /// <summary>
    /// Raid end, whatever the outcome (extraction, death, leaving, transit). As a prefix: we stop
    /// the capture before the game sends /client/match/local/end, so that no send goes out after it.
    /// </summary>
    internal sealed class RaidStoppedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(LocalGame), nameof(LocalGame.Stop));
        }

        /// <summary>What the raid adds up to, so a resumed raid can be checked from the log alone.</summary>
        private static void LogRaidTotals()
        {
            try
            {
                var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                var stats = player?.Profile?.Stats?.Eft;
                if (stats == null)
                {
                    return;
                }

                Plugin.Log.LogInfo(
                    $"Raid over: {stats.Victims?.Count ?? 0} kills, {stats.SessionCounters?.GetAllInt(CounterTag.Exp) ?? 0} experience counted so far"
                );
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Raid totals not read: {ex.Message}");
            }
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            try
            {
                LogRaidTotals();
                SnapshotCapturer.Current?.Stop();
                RecoveryController.Instance?.CheckOnNextMenu();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not stop the capture: {ex}");
            }
        }
    }
}
