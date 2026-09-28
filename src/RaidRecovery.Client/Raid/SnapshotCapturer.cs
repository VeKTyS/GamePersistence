using System;
using System.Collections.Generic;
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

        // What was seen of the loot so far. Lives as long as the raid: a resumed raid starts a new one.
        private readonly LootMemory _lootMemory = new LootMemory();
        private GameWorld _gameWorld;
        private string _sessionId;
        private DateTime _startedAt;
        private string _dateTime;
        private float _lastCaptureAt;
        private bool _capturedOnce;
        private bool _captureRequested;
        private bool _stopped;

        // What one frame may give to the read. At 60 frames per second a frame lasts 16 ms.
        private const double FrameBudgetMs = 3.0;

        // Read in progress, and what to do once it is complete
        private CapturePass _pass;
        private Action _finish;

        // 0 = free, 1 = a read or a send is in progress. Read and written from two threads, hence Interlocked.
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

            if (_pass != null)
            {
                Continue();
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
            if (Begin(player, _captureRequested))
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

        /// <summary>
        /// Starts reading the raid. Returns false when nothing was started, so the caller can try again.
        /// The read itself goes on over the next frames, see Continue.
        /// </summary>
        private bool Begin(Player player, bool manual)
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

            var pass = new CapturePass { Manual = manual };
            var character = new CharacterCapture();
            BotsCapture bots = null;
            SnapshotDto snapshot = null;

            try
            {
                if (Plugin.KeepBots.Value)
                {
                    bots = new BotsCapture();
                    var captured = bots;
                    foreach (var bot in BotsCapture.BotsOf(_gameWorld))
                    {
                        var current = bot;
                        pass.Add("bot", () => captured.Read(current, _gameWorld), required: false);
                    }

                    foreach (var corpse in BotsCapture.CorpsesOf(_gameWorld))
                    {
                        var current = corpse;
                        pass.Add("body", () => captured.Read(current), required: false);
                    }
                }
            }
            catch (Exception ex)
            {
                // Apart from the rest: bots that cannot be listed must not cost the snapshot of the player
                Plugin.Log.LogError($"Could not list the bots, the snapshot goes without them: {ex}");
                bots = null;
                pass = new CapturePass { Manual = manual };
            }

            LootCapture loot = null;
            if (Plugin.KeepLoot.Value)
            {
                try
                {
                    loot = new LootCapture(_lootMemory);
                    loot.Plan(pass, _gameWorld);
                }
                catch (Exception ex)
                {
                    // The server then sorts the loot by what the player carries, as before
                    Plugin.Log.LogError($"Could not list the loot of the map, the snapshot goes without it: {ex}");
                    loot = null;
                }
            }

            // The player comes last: their position and gear are those of the moment the snapshot leaves
            pass.Add("statistics", () => character.ReadStats(player));
            pass.Add("profile", () => character.ReadProfile(player));
            pass.Add(
                "map",
                () =>
                {
                    snapshot = Describe(player, character);
                    snapshot.World = _world.Take(_gameWorld, player);
                }
            );

            _pass = pass;
            _finish = () => Task.Run(() => SendAsync(snapshot, character, bots, loot, pass));
            return true;
        }

        /// <summary>Runs the next steps of the read in progress, and sends the snapshot once the last one is done.</summary>
        private void Continue()
        {
            var pass = _pass;
            if (!pass.Run(FrameBudgetMs))
            {
                return;
            }

            var finish = _finish;
            _pass = null;
            _finish = null;

            if (pass.Failure != null)
            {
                Interlocked.Exchange(ref _sending, 0);
                Plugin.Log.LogError($"Could not read the raid state: {pass.Failure}");
                LastSaveStatus = "Failed, see the log";
                return;
            }

            // Serialization and send: off the main thread, so they do not cost a frame
            finish();
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
                    SecondsPlayed = ReadSecondsPlayed(),
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

        /// <summary>Time played in this raid, from its own start. A resumed raid counts from its resume.</summary>
        private static int? ReadSecondsPlayed()
        {
            var timer = Singleton<AbstractGame>.Instance?.GameTimer;
            if (timer == null || !timer.Started())
            {
                return null;
            }

            return Math.Max(0, (int)timer.PastTime.TotalSeconds);
        }

        private async Task SendAsync(SnapshotDto snapshot, CharacterCapture character, BotsCapture bots, LootCapture loot, CapturePass pass)
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

                if (loot != null)
                {
                    snapshot.World.Gone = loot.GoneIds();
                    snapshot.World.Loose = loot.LooseToJson();
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
                if (pass.Manual || Plugin.LogMeasurements.Value)
                {
                    var size = Encoding.UTF8.GetByteCount(json);
                    Plugin.Log.LogInfo(
                        $"Snapshot saved{(pass.Manual ? " (on request)" : "")}: read {pass.TotalMs:0.00} ms over {pass.Frames} frames (main thread), at most {pass.LongestFrameMs:0.00} ms in one frame, longest step {pass.LongestStep} {pass.LongestStepMs:0.00} ms, serialization {serializeMs:0.00} ms, send {sendMs:0} ms, {size} bytes, {bots?.BotCount ?? 0} bots, {bots?.CorpseCount ?? 0} bodies, {loot?.PresentCount ?? 0} loot items seen, {loot?.GoneCount ?? 0} gone, {loot?.LooseCount ?? 0} dropped or moved"
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
