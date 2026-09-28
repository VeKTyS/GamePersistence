using System;
using System.Linq;
using System.Reflection;
using EFT.UI;
using RaidRecovery.Client.Recovery;
using SPT.Reflection.Patching;

namespace RaidRecovery.Client.Patches
{
    /// <summary>Main menu shown: at game launch, after a raid, or after a menu reload.</summary>
    internal sealed class MenuShownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // MenuScreen has two Show overloads: we target the one the game calls with the profile
            return typeof(MenuScreen).GetMethods().First(m => m.Name == nameof(MenuScreen.Show) && m.GetParameters().Length == 3);
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            try
            {
                RecoveryController.Instance?.OnMenuShown();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not handle the menu being shown: {ex}");
            }
        }
    }
}
