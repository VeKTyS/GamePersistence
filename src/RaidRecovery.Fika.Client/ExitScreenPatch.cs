using System;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine;

namespace RaidRecovery.Fika.Client
{
    /// <summary>
    /// A player taken out of a raid whose host is lost did not end it. The screens the game shows after a
    /// raid say the opposite: the outcome, the experience, and for a scav the transfer of what it carried,
    /// which would move into the stash items of a raid that is not over. None of them is shown: the player
    /// goes straight to the menu. It is a function of the game, not of Fika.
    /// </summary>
    internal sealed class ExitScreenPatch : ModulePatch
    {
        // The screens come a few seconds after the raid is left. Past that, the order is void: it must
        // never reach the screens of a raid the player ended by themselves.
        private const float ValidSeconds = 60f;

        private static float _passUntil;

        /// <summary>true from the moment the player is taken out to the moment the screens are passed.</summary>
        internal static bool PassNext
        {
            get => _passUntil > 0f && Time.unscaledTime <= _passUntil;
            set => _passUntil = value ? Time.unscaledTime + ValidSeconds : 0f;
        }

        protected override MethodBase GetTargetMethod()
        {
            return typeof(TarkovApplication).GetMethod(nameof(TarkovApplication.ShowSessionResult));
        }

        [PatchPrefix]
        private static bool PatchPrefix(TarkovApplication __instance, ref Task __result)
        {
            if (!PassNext)
            {
                return true;
            }

            PassNext = false;
            Plugin.Log.LogInfo("End-of-raid screens not shown: the player did not end the raid, its host was lost");
            __result = BackToMenuAsync(__instance);
            return false;
        }

        /// <summary>What the game does around its screens: the profiles are read again, then the menu is shown.</summary>
        private static async Task BackToMenuAsync(TarkovApplication app)
        {
            try
            {
                var session = app.Session;
                await session.GetProfiles();
                await DataPrepareOperation.SelectProfile(session);
            }
            catch (Exception ex)
            {
                // The menu reads the profile the game already holds
                Plugin.Log.LogWarning($"Profiles not read again before the menu: {ex}");
            }

            try
            {
                await app.ComebackToMainMenu();
                Plugin.Log.LogInfo("Back to the menu, the raid is still open on the server");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"The menu could not be shown after the host was lost: {ex}");
            }
        }
    }
}
