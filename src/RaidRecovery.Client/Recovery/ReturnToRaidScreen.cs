using System;
using System.Linq;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using SPT.Reflection.Utils;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// The "Return to raid" screen of the live game, the one shown after a disconnection. It ships with the
    /// game but a local raid never opens it. We open it ourselves and wire its two buttons to the recovery.
    /// </summary>
    internal static class ReturnToRaidScreen
    {
        /// <returns>false if the screen could not be opened: the caller then falls back on its own window.</returns>
        public static bool TryShow(string map, Action onReconnect, Action onLeave)
        {
            try
            {
                var app = ClientAppUtils.GetMainApp();
                var session = app?.Session;
                var profile = session?.Profile;
                if (profile == null)
                {
                    return false;
                }

                // null is accepted by the screen, which then shows no map: better than no screen at all
                var location = session.LocationSettings?.locations?.Values.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, map, StringComparison.OrdinalIgnoreCase)
                );

                var controller = new ReconnectionScreen.ReconnectionScreenController(
                    profile,
                    location,
                    ESideType.Pmc,
                    // No "back" button: leaving the screen without choosing would keep the snapshot in limbo
                    returnAllowed: false,
                    nextScreenAllowed: true,
                    session
                );

                controller.OnReconnectAction += () => Close(controller, onReconnect);
                controller.OnLeave += () =>
                {
                    // The game hides the bottom bar when leaving a raid for good. Here we stay in the menu.
                    MonoBehaviourSingleton<PreloaderUI>.Instance.SetMenuTaskBarVisibility(true);
                    Close(controller, onLeave);
                };

                controller.ShowScreen(EScreenState.Queued);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"The game's return-to-raid screen could not be opened: {ex.Message}");
                return false;
            }
        }

        private static void Close(ReconnectionScreen.ReconnectionScreenController controller, Action then)
        {
            try
            {
                controller.CloseScreen();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"The return-to-raid screen did not close cleanly: {ex.Message}");
            }

            then();
        }
    }
}
