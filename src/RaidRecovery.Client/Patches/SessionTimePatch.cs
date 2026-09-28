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
                // The time left is taken as it is. It used to be capped by the length the game gives the new raid,
                // but that length is drawn again at each start for a scav: a raid with 15 minutes left came back
                // with 10. The time left was read from a real raid, it cannot exceed what that raid had.
                __result = TimeSpan.FromSeconds(secondsLeft.Value);
                Plugin.Log.LogInfo($"Resumed raid duration: {__result:hh\\:mm\\:ss} instead of {original:hh\\:mm\\:ss}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Resumed raid duration not applied: {ex}");
            }
        }
    }
}
