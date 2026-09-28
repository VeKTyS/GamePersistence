using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using RaidRecovery.Client.Raid;
using RaidRecovery.Client.Recovery;
using SPT.Reflection.Patching;

namespace RaidRecovery.Client.Patches
{
    /// <summary>Raid start: same hook point as SPT's patches.</summary>
    internal sealed class RaidStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        private static void PatchPostfix(GameWorld __instance)
        {
            // Two separate blocks: a failed teleport must not prevent the capture from starting
            try
            {
                RecoveryController.Instance?.OnRaidStarted(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not put the player back: {ex}");
            }

            try
            {
                SnapshotCapturer.Attach(__instance);
            }
            catch (Exception ex)
            {
                // An error in the mod must never prevent the raid from starting
                Plugin.Log.LogError($"Could not start the capture: {ex}");
            }
        }
    }
}
