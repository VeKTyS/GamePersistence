using System;
using System.Reflection;
using EFT;
using EFT.UI;
using EFT.UI.SessionEnd;
using SPT.Reflection.Patching;

namespace RaidRecovery.Fika.Client
{
    /// <summary>
    /// A player taken out of a raid whose host is lost did not leave it: the screen that says they did is
    /// passed at once, the way Fika passes it for a spectator. It is a screen of the game, not of Fika.
    /// </summary>
    internal sealed class ExitScreenPatch : ModulePatch
    {
        /// <summary>true from the moment the player is taken out to the moment the screen is passed.</summary>
        internal static bool PassNext { get; set; }

        protected override MethodBase GetTargetMethod()
        {
            return typeof(SessionResultExitStatus).GetMethod(
                nameof(SessionResultExitStatus.Show),
                new[]
                {
                    typeof(Profile),
                    typeof(PlayerVisualRepresentation),
                    typeof(ESideType),
                    typeof(ExitStatus),
                    typeof(TimeSpan),
                    typeof(IEftSession),
                    typeof(bool),
                }
            );
        }

        [PatchPostfix]
        private static void PatchPostfix(DefaultUIButton ____mainMenuButton)
        {
            if (!PassNext)
            {
                return;
            }

            PassNext = false;
            try
            {
                ____mainMenuButton.OnClick.Invoke();
                Plugin.Log.LogInfo("End-of-raid screen passed: the player did not leave the raid, its host was lost");
            }
            catch (Exception ex)
            {
                // The screen stays: the player closes it by hand and gets to the same menu
                Plugin.Log.LogWarning($"End-of-raid screen not passed, it has to be closed by hand: {ex}");
            }
        }
    }
}
