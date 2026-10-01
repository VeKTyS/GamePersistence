using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using RaidRecovery.Client.Api;
using RaidRecovery.Client.Coop;
using RaidRecovery.Client.Models;
using RaidRecovery.Client.Net;
using RaidRecovery.Client.Raid;
using RaidRecovery.Client.Teammates;
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
            Choosing,
            Restoring,
            ReloadingMenu,
            Launching,
            // The raid was left with the gear carried: the menu is reloaded to show it, nothing is launched
            Keeping,
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

        // Only set for a raid played with others: what the coordinator was told, and what it answered
        private InterruptedRaid _raid;
        private ResumeWay _way = ResumeWay.Restored;

        private const int MinimumResumedSeconds = 60;

        // Margin after the timer starts, to let the game finish placing the player
        private const float PlacementDelaySeconds = 0.5f;
        // Long enough for every bot to be loaded, activated and to have looked around
        private const float DiagnosticsDelaySeconds = 45f;
        private GameWorld _diagnosticsWorld;
        private float _diagnosticsAt;

        // The screen fills its texts when it opens: we let it finish before changing one
        private const float WarningDelaySeconds = 0.3f;
        private float _fixWarningAt;

        private RestoreResult _placement;
        private GameWorld _placementWorld;
        private float _placeAt;

        public static RecoveryController Instance { get; private set; }

        /// <summary>
        /// True while the player of a resumed raid still stands on the spawn point chosen by the game.
        /// A snapshot taken now would record that position instead of the restored one.
        /// </summary>
        public bool IsPlacementPending => _placement != null;

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

            if (_step == Step.Keeping)
            {
                _step = Step.Idle;
                Notify("Raid left: you keep the gear you carried at the last snapshot.");
                return;
            }

            // A raid played with others is never offered as a raid to resume alone
            if (_step != Step.Idle || !_checkDue || !Plugin.Enabled.Value || CoopGuard.IsStoodDown)
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

            WorldRestorer.RestoreEntryPoint(ticket.World);

            // The countdown is a wait anyway: the bots load during it and are ready when the player is placed
            try
            {
                BotsRestorer.Preload(ticket.Bots);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not start loading the bots, they load once the player is placed: {ex}");
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
            var world = _placementWorld;
            var player = world.MainPlayer;
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

            // After the game's own start: doors and extractions are initialized by then
            WorldRestorer.Apply(world, player, ticket.World);
            StanceRestorer.Apply(player, ticket.Stance);

            try
            {
                StatsRestorer.Apply(player, ticket.Stats?.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not restore the raid statistics: {ex}");
            }

            RaidLauncher.Watch(BotsRestorer.ApplyAsync(ticket.Bots), "Putting the bots back");
            _diagnosticsWorld = world;
            _diagnosticsAt = Time.unscaledTime + DiagnosticsDelaySeconds;
            Notify("Raid resumed: gear, health, position, map and bots restored");
        }

        private void Update()
        {
            if (_step == Step.Choosing && _fixWarningAt > 0f && Time.unscaledTime >= _fixWarningAt)
            {
                _fixWarningAt = 0f;
                ReturnToRaidScreen.ReplaceWarning();
            }

            if (_diagnosticsWorld != null && Time.unscaledTime >= _diagnosticsAt)
            {
                var world = _diagnosticsWorld;
                _diagnosticsWorld = null;
                BotsDiagnostics.Log(world, BotsRestorer.RestoredIds);
            }

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
                    Plugin.Log.LogWarning($"This raid cannot be resumed ({pending.Reason ?? "nothing to restore the character from"}), its snapshot is discarded");
                    await RecoveryApi.DiscardAsync().ConfigureAwait(false);
                    // A rule of the server refused it: the player is told why, a missing character is not their concern
                    if (pending.Reason != null)
                    {
                        var reason = pending.Reason;
                        _mainThread.Enqueue(() => Notify(Explain(reason)));
                    }

                    return;
                }

                _mainThread.Enqueue(() => Offer(pending));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Check for an interrupted raid failed: {ex.Message}");
            }
        }

        /// <summary>Shows the choice between going back to the raid and leaving it.</summary>
        private void Offer(PendingResult pending)
        {
            _offer = pending;
            if (Plugin.GameScreen.Value && ReturnToRaidScreen.TryShow(pending.Map, pending.Side, Resume, Discard))
            {
                _fixWarningAt = Time.unscaledTime + WarningDelaySeconds;
                // The game draws the choice: our own window stays closed
                _step = Step.Choosing;
                Plugin.Log.LogInfo("Return-to-raid screen of the game shown");
                return;
            }

            _step = Step.Offering;
            Plugin.Log.LogInfo("Recovery window shown");
        }

        private static InterruptedRaid Describe(PendingResult offer)
        {
            return new InterruptedRaid
            {
                Map = offer.Map,
                Side = offer.Side,
                Coop = offer.Coop,
                SavedAt = offer.SavedAt,
            };
        }

        private void Resume()
        {
            var coordinator = CoopGuard.Coordinator;
            var offer = _offer;
            if (coordinator == null || offer == null)
            {
                RestoreFromServer();
                return;
            }

            _step = Step.Restoring;
            RaidLauncher.Watch(ResumeWithAsync(coordinator, offer), "Recovery");
        }

        /// <summary>
        /// A raid played with others: the coordinator is asked first, before anything is restored. A player
        /// told to wait must still be able to leave and get back the character of before the raid.
        /// </summary>
        private async Task ResumeWithAsync(ICoopCoordinator coordinator, PendingResult offer)
        {
            var raid = Describe(offer);
            ResumeAnswer answer;
            try
            {
                // No ConfigureAwait(false): what follows touches the screens of the game
                answer = await coordinator.BeforeResumeAsync(raid) ?? ResumeAnswer.Restore;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{coordinator.Name} could not say how to go back to the raid: {ex}");
                answer = ResumeAnswer.Refuse($"Raid not resumed: {ex.Message}");
            }

            _mainThread.Enqueue(() =>
            {
                _raid = raid;
                _way = answer.Way;
                switch (answer.Way)
                {
                    case ResumeWay.Refused:
                        Plugin.Log.LogWarning($"{coordinator.Name} refused the recovery: {answer.Reason}");
                        Notify(answer.Reason ?? "The raid cannot be resumed now.");
                        // The snapshot is untouched: the same choice is offered again
                        Offer(offer);
                        return;

                    case ResumeWay.Handled:
                        Plugin.Log.LogInfo($"{coordinator.Name} brings the player back by itself: nothing is restored");
                        _ticket = new RestoreResult
                        {
                            Restored = true,
                            Map = offer.Map,
                            Side = offer.Side,
                            DateTime = offer.DateTime,
                        };
                        Launch();
                        return;

                    default:
                        RestoreFromServer();
                        return;
                }
            });
        }

        private void RestoreFromServer()
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
                RaidLauncher.Start(ticket.Map, ticket.DateTime, ticket.Side, ticket.Squad, _raid, _way, OnLaunchFailed);
                Plugin.Log.LogInfo($"Raid relaunched on {ticket.Map}");
            }
            catch (Exception ex)
            {
                // The profile is already restored: nothing is lost, the raid just has to be started by hand
                Plugin.Log.LogError($"Automatic relaunch failed: {ex}");
                TeammateMods.AfterFailedLaunch();
                _step = Step.Idle;
                Notify($"Profile restored. Start a raid on {ticket.Map} to resume at your position.");
            }
        }

        /// <summary>
        /// The raid played with others could not be launched: the host left in between, or cannot be
        /// reached. Called from the thread the launch failed on.
        /// </summary>
        private void OnLaunchFailed(Exception failure)
        {
            _mainThread.Enqueue(() =>
            {
                TeammateMods.AfterFailedLaunch();
                _ticket = null;
                _step = Step.Idle;
                Notify($"The raid could not be joined: {failure.Message}");
                if (_offer != null)
                {
                    Offer(_offer);
                }
            });
        }

        private void Discard()
        {
            var coordinator = CoopGuard.Coordinator;
            var offer = _offer;
            if (coordinator == null || offer == null)
            {
                DiscardOnServer();
                return;
            }

            _step = Step.Restoring;
            RaidLauncher.Watch(LeaveWithAsync(coordinator, offer), "Leaving the raid");
        }

        /// <summary>
        /// A raid played with others and left for good. When the others went on without this player, the
        /// raid did happen: they keep what they carried. When nobody went on, it is as if it never started.
        /// </summary>
        private async Task LeaveWithAsync(ICoopCoordinator coordinator, PendingResult offer)
        {
            bool keeps;
            try
            {
                keeps = await coordinator.LeaveKeepsGearAsync(Describe(offer));
            }
            catch (Exception ex)
            {
                // The safe side: the character of before the raid, which is what leaving gives alone
                Plugin.Log.LogError($"{coordinator.Name} could not say what leaving gives, the raid is discarded: {ex}");
                keeps = false;
            }

            if (!keeps)
            {
                _mainThread.Enqueue(DiscardOnServer);
                return;
            }

            try
            {
                var result = await RecoveryApi.RestoreAsync().ConfigureAwait(false);
                if (result == null || !result.Restored)
                {
                    _mainThread.Enqueue(() => Fail($"The gear could not be kept: {result?.Reason ?? "no response"}"));
                    return;
                }

                await RecoveryApi.DiscardAsync().ConfigureAwait(false);
                _mainThread.Enqueue(() =>
                {
                    _offer = null;
                    _step = Step.Keeping;
                    Plugin.Log.LogInfo("Raid left with the gear of the last snapshot, reloading the menu");
                    RaidLauncher.ReloadMenu();
                });
            }
            catch (Exception ex)
            {
                _mainThread.Enqueue(() => Fail($"The gear could not be kept: {ex.Message}"));
            }
        }

        private void DiscardOnServer()
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

        private static string Explain(string reason)
        {
            switch (reason)
            {
                case "TooManyResumes":
                    return "Raid not resumed: it was already resumed as many times as the server allows.";
                case "DeathWasImminent":
                    return "Raid not resumed: your character was about to die when it was cut.";
                default:
                    return $"Raid not resumed: {reason}";
            }
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
