using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RaidRecovery.Client.Models;
using RaidRecovery.Client.Net;
using RaidRecovery.Client.Recovery;
using SPT.Reflection.Utils;
using UnityEngine;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// Capture loop of a raid. The component is attached to the GameWorld object: when the game destroys
    /// the world at the end of the raid, the loop goes away with it, with nothing to unsubscribe.
    /// </summary>
    internal sealed class SnapshotCapturer : MonoBehaviour
    {
        public const string HideoutLocationId = "hideout";

        private readonly WorldCapture _world = new WorldCapture();
        private GameWorld _gameWorld;
        private string _sessionId;
        private DateTime _startedAt;
        private string _dateTime;
        private float _lastCaptureAt;
        private bool _capturedOnce;
        private bool _captureRequested;
        private bool _stopped;

        // 0 = free, 1 = a send is in progress. Read and written from two threads, hence Interlocked.
        private int _sending;

        public static SnapshotCapturer Current { get; private set; }

        /// <summary>Outcome of the last send, shown in the configuration menu. Written from the send thread.</summary>
        public volatile string LastSaveStatus = "No snapshot yet";

        public static void Attach(GameWorld gameWorld)
        {
            if (!Plugin.Enabled.Value || gameWorld == null)
            {
                return;
            }

            // The hideout is a GameWorld too: nothing is captured there
            if (string.Equals(gameWorld.LocationId, HideoutLocationId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var capturer = gameWorld.gameObject.AddComponent<SnapshotCapturer>();
            capturer._gameWorld = gameWorld;
            capturer._startedAt = DateTime.UtcNow;
            capturer._sessionId = capturer._startedAt.ToString("yyyyMMddHHmmss");
            // The time slot (current time or shifted by 12 h) is chosen at launch and no longer changes during the raid
            capturer._dateTime = ClientAppUtils.GetMainApp()?._raidSettings?.SelectedDateTime.ToString();
            Current = capturer;

            Plugin.Log.LogInfo($"Capture started on {gameWorld.LocationId}, raid {capturer._sessionId}");
        }

        /// <summary>Normal end of the raid: the server purges on its side, we only stop sending.</summary>
        public void Stop()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            if (Current == this)
            {
                Current = null;
            }

            Plugin.Log.LogInfo($"Capture stopped, raid {_sessionId}");
            Destroy(this);
        }

        private void Update()
        {
            if (_stopped || _gameWorld == null)
            {
                return;
            }

            var player = _gameWorld.MainPlayer;
            if (player == null)
            {
                return;
            }

            // Death is a normal end: we erase right away, without waiting for the end-of-raid screen
            if (player.HealthController != null && !player.HealthController.IsAlive)
            {
                Stop();
                DiscardInBackground();
                return;
            }

            // Resumed raid, player not put back yet: the position is the game's spawn point, not the restored one
            if (RecoveryController.Instance != null && RecoveryController.Instance.IsPlacementPending)
            {
                return;
            }

            if (Plugin.SaveNowKey.Value.IsDown())
            {
                _captureRequested = true;
            }

            // The interval is read again on every frame: a change in the menu applies to the wait in progress
            var interval = Mathf.Clamp(Plugin.IntervalSeconds.Value, Plugin.MinIntervalSeconds, Plugin.MaxIntervalSeconds);
            var due = !_capturedOnce || Time.unscaledTime >= _lastCaptureAt + interval;
            if (!due && !_captureRequested)
            {
                return;
            }

            // A request made while a send is in progress stays pending and is served on a later frame
            if (Capture(player, _captureRequested))
            {
                _captureRequested = false;
                _capturedOnce = true;
                _lastCaptureAt = Time.unscaledTime;
            }
        }

        /// <summary>Asks for a snapshot right away. It is taken on the next frame, on the main thread.</summary>
        public void RequestCapture()
        {
            _captureRequested = true;
            LastSaveStatus = "Saving...";
        }

        private void OnDestroy()
        {
            _stopped = true;
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>Returns false when nothing was captured, so the caller can try again.</summary>
        private bool Capture(Player player, bool manual)
        {
            // A previous send is not finished: we skip this capture rather than piling up sends
            if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0)
            {
                // A manual request is retried on every frame: logging here would flood the log
                if (!manual)
                {
                    Plugin.Log.LogWarning("Previous send still in progress, capture skipped");
                }

                return !manual;
            }

            // Reading the game state: has to happen on the main thread, so it must stay as short as possible
            var stopwatch = Stopwatch.StartNew();
            SnapshotDto snapshot;
            CharacterCapture character;
            try
            {
                character = CharacterCapture.Take(player);
                snapshot = Describe(player, character);
                snapshot.World = _world.Take(_gameWorld, player);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _sending, 0);
                Plugin.Log.LogError($"Could not read the raid state: {ex}");
                LastSaveStatus = "Failed, see the log";
                // Counted as done: a read that fails would fail again on the next frame
                return true;
            }

            // Apart from the rest: bots that cannot be read must not cost the snapshot of the player
            BotsCapture bots = null;
            if (Plugin.KeepBots.Value)
            {
                try
                {
                    bots = BotsCapture.Take(_gameWorld);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Could not read the bots, the snapshot goes without them: {ex}");
                }
            }

            var readMs = stopwatch.Elapsed.TotalMilliseconds;

            // Serialization and send: off the main thread, so they do not cost a frame
            Task.Run(() => SendAsync(snapshot, character, bots, readMs, manual));
            return true;
        }

        private SnapshotDto Describe(Player player, CharacterCapture character)
        {
            return new SnapshotDto
            {
                SessionId = _sessionId,
                Map = _gameWorld.LocationId,
                Raid = new RaidDto
                {
                    StartedAt = _startedAt,
                    DateTime = _dateTime,
                    GameTime = _gameWorld.GameDateTime?.Calculate().ToString("HH:mm"),
                    SecondsLeft = ReadSecondsLeft(),
                    Side = player.Side == EPlayerSide.Savage ? "Savage" : "Pmc",
                },
                Player = new PlayerDto
                {
                    Position = new PositionDto { X = character.Position.x, Y = character.Position.y, Z = character.Position.z },
                    Rotation = new RotationDto { Yaw = character.Rotation.x, Pitch = character.Rotation.y },
                },
            };
        }

        /// <summary>Time left on the raid timer, or null as long as the starting countdown is not over.</summary>
        private static int? ReadSecondsLeft()
        {
            var timer = Singleton<AbstractGame>.Instance?.GameTimer;
            if (timer == null || !timer.Started())
            {
                return null;
            }

            return Math.Max(0, (int)timer.EscapeTimeSeconds());
        }

        private async Task SendAsync(SnapshotDto snapshot, CharacterCapture character, BotsCapture bots, double readMs, bool manual)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                snapshot.Player.Profile = new JRaw(character.ToJson());
                if (character.StatsJson != null)
                {
                    snapshot.Player.Stats = new JRaw(character.StatsJson);
                }
                if (bots != null)
                {
                    snapshot.Bots = bots.BotsToDtos();
                    snapshot.World.Corpses = bots.CorpsesToJson();
                }

                var json = JsonConvert.SerializeObject(snapshot);
                var serializeMs = stopwatch.Elapsed.TotalMilliseconds;

                // The raid ended while the send was being prepared
                if (_stopped)
                {
                    return;
                }

                stopwatch.Restart();
                var result = await RecoveryApi.SaveAsync(json).ConfigureAwait(false);
                var sendMs = stopwatch.Elapsed.TotalMilliseconds;

                if (result == null || !result.Saved)
                {
                    Plugin.Log.LogWarning($"Snapshot not saved: {result?.Reason ?? "no response from the server"}");
                    LastSaveStatus = "Failed, see the log";
                    return;
                }

                LastSaveStatus = $"Saved at {DateTime.Now:HH:mm:ss}";
                // A save asked by hand is always logged: it is the proof the player is looking for
                if (manual || Plugin.LogMeasurements.Value)
                {
                    var size = Encoding.UTF8.GetByteCount(json);
                    Plugin.Log.LogInfo(
                        $"Snapshot saved{(manual ? " (on request)" : "")}: read {readMs:0.00} ms (main thread), serialization {serializeMs:0.00} ms, send {sendMs:0} ms, {size} bytes, {bots?.BotCount ?? 0} bots, {bots?.CorpseCount ?? 0} bodies"
                    );
                }
            }
            catch (Exception ex)
            {
                // A failed send is dropped: the next one will replace it
                Plugin.Log.LogWarning($"Sending the snapshot failed: {ex.Message}");
                LastSaveStatus = "Failed, see the log";
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        private static void DiscardInBackground()
        {
            Task.Run(async () =>
            {
                try
                {
                    await RecoveryApi.DiscardAsync().ConfigureAwait(false);
                    Plugin.Log.LogInfo("Death in raid: snapshot erased");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Erasing the snapshot failed: {ex.Message}");
                }
            });
        }
    }
}
