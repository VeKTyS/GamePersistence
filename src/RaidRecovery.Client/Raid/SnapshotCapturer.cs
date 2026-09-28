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

        private GameWorld _gameWorld;
        private string _sessionId;
        private DateTime _startedAt;
        private string _dateTime;
        private float _nextCaptureAt;
        private bool _stopped;

        // 0 = free, 1 = a send is in progress. Read and written from two threads, hence Interlocked.
        private int _sending;

        public static SnapshotCapturer Current { get; private set; }

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

            if (Time.unscaledTime < _nextCaptureAt)
            {
                return;
            }

            _nextCaptureAt = Time.unscaledTime + Mathf.Clamp(Plugin.IntervalSeconds.Value, Plugin.MinIntervalSeconds, Plugin.MaxIntervalSeconds);
            Capture(player);
        }

        private void OnDestroy()
        {
            _stopped = true;
            if (Current == this)
            {
                Current = null;
            }
        }

        private void Capture(Player player)
        {
            // A previous send is not finished: we skip this capture rather than piling up sends
            if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0)
            {
                Plugin.Log.LogWarning("Previous send still in progress, capture skipped");
                return;
            }

            // Reading the game state: has to happen on the main thread, so it must stay as short as possible
            var stopwatch = Stopwatch.StartNew();
            SnapshotDto snapshot;
            CharacterCapture character;
            try
            {
                character = CharacterCapture.Take(player);
                snapshot = Describe(player, character);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _sending, 0);
                Plugin.Log.LogError($"Could not read the raid state: {ex}");
                return;
            }

            var readMs = stopwatch.Elapsed.TotalMilliseconds;

            // Serialization and send: off the main thread, so they do not cost a frame
            Task.Run(() => SendAsync(snapshot, character, readMs));
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

        private async Task SendAsync(SnapshotDto snapshot, CharacterCapture character, double readMs)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                snapshot.Player.Profile = new JRaw(character.ToJson());
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
                    return;
                }

                if (Plugin.LogMeasurements.Value)
                {
                    var size = Encoding.UTF8.GetByteCount(json);
                    Plugin.Log.LogInfo(
                        $"Snapshot saved: read {readMs:0.00} ms (main thread), serialization {serializeMs:0.00} ms, send {sendMs:0} ms, {size} bytes"
                    );
                }
            }
            catch (Exception ex)
            {
                // A failed send is dropped: the next one will replace it
                Plugin.Log.LogWarning($"Sending the snapshot failed: {ex.Message}");
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
