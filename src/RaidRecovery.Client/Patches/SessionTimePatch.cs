using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using RaidRecovery.Client.Recovery;
using SPT.Reflection.Patching;

namespace RaidRecovery.Client.Patches
{
    /// <summary>
    /// Raid duration, at the moment the game computes it to create the match. By setting it here rather than
    /// adjusting the timer afterwards, everything that depends on it starts from the right value: display, extractions, raid end.
    /// </summary>
    internal sealed class SessionTimePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LocalGameSessionTime));
        }

        [PatchPostfix]
        private static void PatchPostfix(TarkovApplication __instance, ref TimeSpan __result)
        {
            try
            {
                var map = __instance._raidSettings?.SelectedLocation?.Id;
                var secondsLeft = RecoveryController.Instance?.SecondsLeftFor(map);
                if (secondsLeft == null)
                {
                    return;
                }

                var original = __result;
                // Never longer than the normal raid: a snapshot must not be a way to extend a raid
                __result = TimeSpan.FromSeconds(Math.Min(secondsLeft.Value, original.TotalSeconds));
                Plugin.Log.LogInfo($"Resumed raid duration: {__result:hh\\:mm\\:ss} instead of {original:hh\\:mm\\:ss}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Resumed raid duration not applied: {ex}");
            }
        }
    }
}
