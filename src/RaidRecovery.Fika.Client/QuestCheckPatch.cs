using System.Reflection;
using EFT.Quests;
using SPT.Reflection.Patching;

namespace RaidRecovery.Fika.Client
{
    /// <summary>
    /// When a raid ends, the game reads how the player left it and fails the quests that forbid it. A
    /// player taken out because the host is lost left nothing: for them the reading is skipped. It is a
    /// function of the game, not of Fika.
    /// </summary>
    internal sealed class QuestCheckPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ConditionalController<Quest>).GetMethod(nameof(ConditionalController<Quest>.CheckExitConditionCounters));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            if (!ExitScreenPatch.PassNext)
            {
                return true;
            }

            Plugin.Log.LogInfo("Quests not read at the end of this raid: the player did not leave it, its host was lost");
            return false;
        }
    }
}
