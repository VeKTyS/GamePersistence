using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using RaidRecovery.Client.Models;
using RaidRecovery.Client.Net;
using RaidRecovery.Client.Raid;
using UnityEngine;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// Runs the recovery of an interrupted raid, from the detection at the menu to the teleport:
    /// offer, apply the state to the profile, reload the menu, relaunch the raid, put the player back.
    /// </summary>
    internal sealed class RecoveryController : MonoBehaviour
    {
        private enum Step
        {
            Idle,
            Offering,
            Restoring,
            ReloadingMenu,
            Launching,
        }

        private const int WindowId = 0x52524543;
        private const float WindowWidth = 480f;
        private const float WindowHeight = 210f;

        // Server responses arrive on another thread, but the Unity API can only be used from the
        // main thread: the work is queued there and Update picks it up on the next frame.
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();

        private Step _step = Step.Idle;
        private bool _checkDue = true;
        private PendingResult _offer;
        private RestoreResult _ticket;

        private const int MinimumResumedSeconds = 60;

        // Margin after the timer starts, to let the game finish placing the player
        private const float PlacementDelaySeconds = 0.5f;
        private RestoreResult _placement;
        private GameWorld _placementWorld;
        private float _placeAt;

        public static RecoveryController Instance { get; private set; }

        /// <summary>
        /// Attaches to the plugin's object, not to one we create: the game destroys the objects it does not know,
        /// and BepInEx hides its own (HideManagerGameObject) precisely to escape that.
        /// </summary>
        public static void Create(GameObject pluginObject)
        {
            Instance = pluginObject.AddComponent<RecoveryController>();
        }

        /// <summary>After a raid, the next time the menu is shown checks again whether a snapshot remains.</summary>
        public void CheckOnNextMenu()
        {
            _checkDue = true;
        }

        public void OnMenuShown()
        {
            // A component destroyed by Unity can still be called from C#, but no longer receives Update or OnGUI.
            // Unity's null comparison is the only one that reveals it: we want to know rather than fail silently.
            if (this == null)
            {
                Plugin.Log.LogError("The recovery component was destroyed by the game: no window can be shown");
                return;
            }

            if (_step == Step.ReloadingMenu)
            {
                // The menu was just reloaded with the restored profile: the raid can be relaunched
                Launch();
                return;
            }

            if (_step != Step.Idle || !_checkDue || !Plugin.Enabled.Value)
            {
                return;
            }

            _checkDue = false;
            Task.Run(CheckPendingAsync);
        }

        /// <summary>
        /// Time to give to the raid being created, or null if it is not a recovery on this map.
        /// One minute floor: resuming a raid that ends during its own loading would make no sense.
        /// </summary>
        public int? SecondsLeftFor(string map)
        {
            var ticket = _ticket;
            if (ticket?.SecondsLeft == null || !string.Equals(ticket.Map, map, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Math.Max(MinimumResumedSeconds, ticket.SecondsLeft.Value);
        }

        /// <summary>At the start of the resumed raid, puts the player back where the snapshot left them.</summary>
        public void OnRaidStarted(GameWorld gameWorld)
        {
            var ticket = _ticket;
            _ticket = null;
            _step = Step.Idle;

            if (ticket?.Position == null || gameWorld?.MainPlayer == null)
            {
                return;
            }

            if (string.Equals(gameWorld.LocationId, SnapshotCapturer.HideoutLocationId, StringComparison.OrdinalIgnoreCase))
            {
                // The hideout also loads as a game world: the ticket applies to the raid that follows
                _ticket = ticket;
                return;
            }

            // A ticket taken on one map makes no sense on another: the coordinates would land anywhere
            if (!string.Equals(gameWorld.LocationId, ticket.Map, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogWarning($"Raid launched on {gameWorld.LocationId} instead of {ticket.Map}: position not restored");
                return;
            }

            // OnGameStarted fires before the countdown ends. The game then calls Spawn(), which puts the player back
            // on their spawn point: moving now would be overwritten. So we wait for the real start.
            _placement = ticket;
            _placementWorld = gameWorld;
            _placeAt = float.MaxValue;
        }

        /// <summary>Puts the player back once the raid timer is running, that is after the game's Spawn().</summary>
        private void PlaceWhenRaidRuns()
        {
            if (_placement == null)
            {
                return;
            }

            // The raid was left before it started
            if (_placementWorld == null || _placementWorld.MainPlayer == null)
            {
                _placement = null;
                return;
            }

            var timer = Singleton<AbstractGame>.Instance?.GameTimer;
            if (timer == null || !timer.Started())
            {
                return;
            }

            if (_placeAt == float.MaxValue)
            {
                _placeAt = Time.unscaledTime + PlacementDelaySeconds;
                return;
            }

            if (Time.unscaledTime < _placeAt)
            {
                return;
            }

            var ticket = _placement;
            var player = _placementWorld.MainPlayer;
            _placement = null;
            _placementWorld = null;

            var position = new Vector3(ticket.Position.X, ticket.Position.Y, ticket.Position.Z);
            var from = player.Position;
            player.Teleport(position, true);
            if (ticket.Rotation != null)
            {
                player.Rotation = new Vector2(ticket.Rotation.Yaw, ticket.Rotation.Pitch);
            }

            Plugin.Log.LogInfo($"Player moved from {from} to {position}, position read afterwards: {player.Position}");
            Notify("Raid resumed: gear, health and position restored");
        }

        private void Update()
        {
            try
            {
                PlaceWhenRaidRuns();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not put the player back: {ex}");
                _placement = null;
            }

            while (_mainThread.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Recovery interrupted: {ex}");
                    _step = Step.Idle;
                }
            }
        }

        private void OnGUI()
        {
            if (_step != Step.Offering || _offer == null)
            {
                return;
            }

            var area = new Rect((Screen.width - WindowWidth) / 2f, (Screen.height - WindowHeight) / 2f, WindowWidth, WindowHeight);
            GUI.ModalWindow(WindowId, area, DrawWindow, "Raid Recovery");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(8f);
            GUILayout.Label("The previous raid was interrupted before it ended.");
            GUILayout.Label($"Map: {_offer.Map}");
            if (_offer.SecondsLeft.HasValue)
            {
                GUILayout.Label($"Time left: {_offer.SecondsLeft.Value / 60} min");
            }

            if (_offer.SavedAt.HasValue)
            {
                GUILayout.Label($"Last snapshot: {_offer.SavedAt.Value.ToLocalTime():HH:mm:ss}");
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Resume", GUILayout.Height(34f)))
            {
                Resume();
            }

            if (GUILayout.Button("Discard", GUILayout.Height(34f)))
            {
                Discard();
            }

            GUILayout.EndHorizontal();
        }

        private async Task CheckPendingAsync()
        {
            try
            {
                var pending = await RecoveryApi.GetPendingAsync().ConfigureAwait(false);
                if (pending == null || !pending.Pending)
                {
                    Plugin.Log.LogInfo("No interrupted raid to resume");
                    return;
                }

                Plugin.Log.LogWarning($"Interrupted raid detected: {pending.Map}, snapshot from {pending.SavedAt:u}");
                if (!pending.Restorable)
                {
                    // Snapshot without a character (scav raid, capture from an older version): nothing to offer
                    Plugin.Log.LogWarning("This snapshot does not hold what is needed to restore the character, it is discarded");
                    await RecoveryApi.DiscardAsync().ConfigureAwait(false);
                    return;
                }

                _mainThread.Enqueue(() =>
                {
                    _offer = pending;
                    _step = Step.Offering;
                    Plugin.Log.LogInfo("Recovery window shown");
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Check for an interrupted raid failed: {ex.Message}");
            }
        }

        private void Resume()
        {
            _step = Step.Restoring;
            Task.Run(async () =>
            {
                try
                {
                    var result = await RecoveryApi.RestoreAsync().ConfigureAwait(false);
                    _mainThread.Enqueue(() => OnRestored(result));
                }
                catch (Exception ex)
                {
                    _mainThread.Enqueue(() => Fail($"Recovery failed: {ex.Message}"));
                }
            });
        }

        private void OnRestored(RestoreResult result)
        {
            if (result == null || !result.Restored)
            {
                Fail($"Recovery refused by the server: {result?.Reason ?? "no response"}");
                return;
            }

            _ticket = result;
            _step = Step.ReloadingMenu;
            Plugin.Log.LogInfo("State applied to the profile, reloading the menu");

            // The server just changed the profile, but the game still holds the previous one in memory. We go through
            // the path the game takes after every raid: it requests the profile again and rebuilds the menu with it.
            RaidLauncher.ReloadMenu();
        }

        private void Launch()
        {
            var ticket = _ticket;
            if (ticket == null)
            {
                _step = Step.Idle;
                return;
            }

            if (!Plugin.AutoLaunch.Value)
            {
                _step = Step.Idle;
                Notify($"Profile restored. Start a raid on {ticket.Map} to resume at your position.");
                return;
            }

            _step = Step.Launching;
            try
            {
                RaidLauncher.Start(ticket.Map, ticket.DateTime);
                Plugin.Log.LogInfo($"Raid relaunched on {ticket.Map}");
            }
            catch (Exception ex)
            {
                // The profile is already restored: nothing is lost, the raid just has to be started by hand
                Plugin.Log.LogError($"Automatic relaunch failed: {ex}");
                _step = Step.Idle;
                Notify($"Profile restored. Start a raid on {ticket.Map} to resume at your position.");
            }
        }

        private void Discard()
        {
            _step = Step.Idle;
            _offer = null;
            Task.Run(async () =>
            {
                try
                {
                    await RecoveryApi.DiscardAsync().ConfigureAwait(false);
                    Plugin.Log.LogInfo("Interrupted raid discarded");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Discarding the snapshot failed: {ex.Message}");
                }
            });
        }

        private void Fail(string message)
        {
            Plugin.Log.LogError(message);
            _ticket = null;
            _offer = null;
            _step = Step.Idle;
            Notify(message);
        }

        private static void Notify(string message)
        {
            try
            {
                NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Long);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Notification failed: {ex.Message}");
            }
        }
    }
}
