using System;
using System.Linq;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using SPT.Reflection.Utils;
using TMPro;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// The "Return to raid" screen of the live game, the one shown after a disconnection. It ships with the
    /// game but a local raid never opens it. We open it ourselves and wire its two buttons to the recovery.
    /// </summary>
    internal static class ReturnToRaidScreen
    {
        /// <returns>false if the screen could not be opened: the caller then falls back on its own window.</returns>
        public static bool TryShow(string map, string side, Action onReconnect, Action onLeave)
        {
            try
            {
                var app = ClientAppUtils.GetMainApp();
                var session = app?.Session;
                var isScav = RaidLauncher.IsScav(side);
                var profile = isScav ? session?.ProfileOfPet : session?.Profile;
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
                    isScav ? ESideType.Savage : ESideType.Pmc,
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

        private const string Warning =
            "This raid was interrupted. Reconnect to go back to it where you left it, or leave it and keep the gear you had before it.";

        // The word the warning of the game uses, and the sentence that replaces it in the same language.
        // A language we have the word for but no sentence gets the English one.
        private static readonly (string Word, string Sentence)[] Penalties =
        {
            ("pénalité", "Ce raid a été interrompu. Reconnectez-vous pour le reprendre là où vous l'aviez laissé, ou quittez-le et gardez l'équipement que vous aviez avant."),
            ("penalite", "Ce raid a été interrompu. Reconnectez-vous pour le reprendre là où vous l'aviez laissé, ou quittez-le et gardez l'équipement que vous aviez avant."),
            ("penalty", Warning),
            ("штраф", Warning),
            ("penalización", Warning),
            ("penalidade", Warning),
        };

        /// <summary>
        /// The screen comes from the live game and warns about a penalty for leaving. The mod has none:
        /// leaving gives back the profile of before the raid. We replace the sentence rather than let it lie.
        /// </summary>
        public static void ReplaceWarning()
        {
            try
            {
                var screen = UnityEngine.Object.FindObjectOfType<ReconnectionScreen>();
                if (screen == null)
                {
                    return;
                }

                var replaced = 0;
                foreach (var text in screen.GetComponentsInChildren<TMP_Text>(true))
                {
                    var current = text.text ?? string.Empty;
                    var penalty = Penalties.FirstOrDefault(entry => current.IndexOf(entry.Word, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (penalty.Sentence != null)
                    {
                        text.text = penalty.Sentence;
                        replaced++;
                    }
                    else if (Plugin.LogMeasurements.Value && current.Length > 40)
                    {
                        // Written down so a warning in a language we did not foresee can be found
                        Plugin.Log.LogInfo($"Text of the return-to-raid screen left as it is: {current}");
                    }
                }

                Plugin.Log.LogInfo($"Warning of the return-to-raid screen: {replaced} sentence(s) replaced");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"The warning of the return-to-raid screen could not be replaced: {ex.Message}");
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
