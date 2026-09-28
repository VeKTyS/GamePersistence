using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using RaidRecovery.Client.Recovery;
using SPT.Reflection.Patching;

namespace RaidRecovery.Client.Patches
{
    /// <summary>
    /// A body read from the loot of a raid loads its head and clothes from the online set of the game, which
    /// does not exist in a local raid: the load fails and the body stays invisible. The game never takes this
    /// path in a local raid, so it never noticed. We load from the local set instead.
    /// </summary>
    internal sealed class CorpseAssetsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Corpse), nameof(Corpse.LoadCustomizationResources));
        }

        [PatchPrefix]
        private static bool PatchPrefix(List<string> customizationsData, ref Task __result)
        {
            __result = LoadAsync(customizationsData);
            // The original is replaced, not completed: it would fail right after us
            return false;
        }

        private static async Task LoadAsync(List<string> customizations)
        {
            try
            {
                var solver = Singleton<CustomizationSolver>.Instance;
                await GameAssets.LoadAsync(customizations.Select(id => solver.GetBundle(id)));
            }
            catch (Exception ex)
            {
                // The body stays invisible but lootable, as before the patch
                Plugin.Log.LogError($"Could not load the look of a body: {ex}");
            }
        }
    }
}
