using System;
using System.Reflection;
using EFT;
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

        [PatchPrefix]
        private static void PatchPrefix()
        {
            try
            {
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
