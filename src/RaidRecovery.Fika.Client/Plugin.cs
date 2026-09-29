using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using Fika.Core.Main.GameMode;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using HarmonyLib;
using RaidRecovery.Client.Api;
using UnityEngine;

namespace RaidRecovery.Fika.Client
{
    /// <summary>
    /// Bridge between Raid Recovery and Fika. It saves and restores nothing by itself: it tells the base mod
    /// when to do it, and carries between the players what the base mod needs.
    /// </summary>
    [BepInPlugin(Guid, "Raid Recovery Fika", Version)]
    [BepInDependency(RaidRecovery.Client.Plugin.Guid, "1.2.1")]
    [BepInDependency("com.fika.core")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.vektys.raidrecovery.fika";
        public const string Version = "0.2.2";

        // Once the host clicked "Start raid", Fika only lets in the players it already knows
        internal const string WaitForEveryone = "Wait for ALL your players to join before you click \"Start raid\". A player who is not in by then cannot come back into this raid.";

        private const string WaitForEveryoneShort = "Wait for ALL your players before you start the raid: a player who is not in cannot come back";

        // Time left to read the message before the raid is left
        private const float LeaveDelaySeconds = 6f;

        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> LeaveWhenHostIsLost { get; private set; }

        // true from the start of a raid this player joined to its end
        private static bool _joinedRaidRuns;
        private static float _leaveAt;
        private static int _tick;

        // true from the creation of a resumed raid to the moment its host reads the warning
        private static bool _hostIsToWarn;
        private static float _lookAgainAt;

        // Doors received before the raid of this player started: applied when it does
        private static Dictionary<string, byte> _doorsToApply;

        private void Awake()
        {
            Log = Logger;

            // An exception here would leave Raid Recovery without a coordinator, and so stood down: nothing
            // is saved, nothing is offered. That is the safe side, we only make sure it is written.
            try
            {
                LeaveWhenHostIsLost = Config.Bind(
                    "Recovery",
                    "Go back to the menu when the host is lost",
                    true,
                    "When the host of the raid is gone, Fika leaves you in a raid where nothing moves. When on, you are taken back to the menu, where you can rejoin the raid once the host has resumed it. When off, you stay in the raid: leaving it by yourself ends it for good."
                );

                new ExitScreenPatch().Enable();
                new QuestCheckPatch().Enable();

                RaidRecoveryApi.Register(new FikaCoordinator());
                RaidRecoveryApi.SnapshotStarted += OnSnapshotStarted;
                RaidRecoveryApi.DoorsRestored += OnDoorsRestored;

                FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkCreated);
                FikaEventDispatcher.SubscribeEvent<AbstractGameCreatedEvent>(OnGameCreated);
                FikaEventDispatcher.SubscribeEvent<FikaRaidStartedEvent>(OnRaidStarted);
                FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(OnGameEnded);
                FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
                FikaEventDispatcher.SubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);

                Log.LogInfo($"Raid Recovery Fika {Version} loaded");
                // What an issue needs first: which versions ran together
                Log.LogInfo($"Versions: Raid Recovery {RaidRecovery.Client.Plugin.Version}, Fika {VersionOf("com.fika.core")}, game {Application.version}. Go back to the menu when the host is lost: {LeaveWhenHostIsLost.Value}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Raid Recovery Fika {Version} could not load, raids played with Fika are NOT saved: {ex}");
            }
        }

        private void Update()
        {
            Guarded("Watching the host", WatchTheHost);
            Guarded("Warning the host", WarnTheHost);
        }

        /// <summary>
        /// Fika calls its listeners in a row, without protection: an exception of ours would stop the ones
        /// that come after, and what Fika was doing. Nothing of ours may ever leave a handler.
        /// </summary>
        private static void Guarded(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.LogError($"{what} failed: {ex}");
            }
        }

        private static string VersionOf(string guid)
        {
            return Chainloader.PluginInfos.TryGetValue(guid, out var info) ? info.Metadata.Version.ToString() : "not found";
        }

        // The role is read at each raid, never kept: the same player hosts one raid and joins the next
        private static string Role => FikaBackendUtils.IsServer ? "host" : "client";

        private static void OnNetworkCreated(FikaNetworkManagerCreatedEvent e)
        {
            Guarded(
                "Network created",
                () =>
                {
                    // On every new manager, host and client alike: a packet Fika does not know raises errors without end
                    e.Manager.RegisterPacket<SaveSignalPacket>(OnSaveSignal);
                    e.Manager.RegisterPacket<DoorsPacket>(OnDoors);
                    _tick = 0;
                    _doorsToApply = null;
                    Log.LogInfo($"Network ready, role: {Role}");
                }
            );
        }

        /// <summary>The host starts reading a snapshot: the players who joined are told to save now.</summary>
        private static void OnSnapshotStarted()
        {
            Guarded(
                "Save signal",
                () =>
                {
                    if (!FikaBackendUtils.IsServer || !Singleton<FikaServer>.Instantiated)
                    {
                        return;
                    }

                    var server = Singleton<FikaServer>.Instance;
                    if (server.NetServer == null || server.NetServer.ConnectedPeersCount == 0)
                    {
                        return;
                    }

                    var packet = new SaveSignalPacket { Tick = ++_tick };
                    server.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
                    Log.LogInfo($"The host saves (snapshot {packet.Tick}): {server.NetServer.ConnectedPeersCount} player(s) told to save their character");
                }
            );
        }

        /// <summary>
        /// The host put its doors back as the snapshot has them. Fika gave the doors to the players when
        /// they joined, before that: they are told again.
        /// </summary>
        private static void OnDoorsRestored(IReadOnlyDictionary<string, byte> changed)
        {
            Guarded(
                "Doors sent",
                () =>
                {
                    if (!FikaBackendUtils.IsServer || !Singleton<FikaServer>.Instantiated)
                    {
                        return;
                    }

                    var server = Singleton<FikaServer>.Instance;
                    var players = server.NetServer?.ConnectedPeersCount ?? 0;
                    if (players == 0)
                    {
                        Log.LogInfo($"{changed.Count} door(s) restored, no player to tell");
                        return;
                    }

                    var packet = new DoorsPacket { States = new Dictionary<string, byte>(changed) };
                    server.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
                    Log.LogInfo($"{changed.Count} restored door(s) sent to {players} player(s)");
                }
            );
        }

        private static void OnDoors(DoorsPacket packet)
        {
            Guarded(
                "Doors received",
                () =>
                {
                    if (FikaBackendUtils.IsServer || packet.States == null)
                    {
                        return;
                    }

                    if (!_joinedRaidRuns)
                    {
                        _doorsToApply = packet.States;
                        Log.LogInfo($"{packet.States.Count} door(s) received from the host, kept until the raid starts");
                        return;
                    }

                    ApplyDoors(packet.States);
                }
            );
        }

        private static void ApplyDoors(Dictionary<string, byte> states)
        {
            var changed = RaidRecoveryApi.ApplyDoors(states);
            Log.LogInfo($"Doors of the host applied: {changed} changed out of {states.Count} received");
        }

        private static void OnSaveSignal(SaveSignalPacket packet)
        {
            Guarded(
                "Save signal received",
                () =>
                {
                    if (FikaBackendUtils.IsServer)
                    {
                        return;
                    }

                    Log.LogInfo($"The host saves (snapshot {packet.Tick}): saving the character");
                    RaidRecoveryApi.RequestSnapshot();
                }
            );
        }

        /// <summary>
        /// The raid exists but has not started: its length can still be changed. The host sends it to the
        /// clients afterwards, so they get the time left without anything more.
        /// </summary>
        private static void OnGameCreated(AbstractGameCreatedEvent e)
        {
            Guarded(
                "Game created",
                () =>
                {
                    Log.LogInfo($"Raid created, role: {Role}, reconnecting: {FikaBackendUtils.IsReconnect}");
                    if (!FikaBackendUtils.IsServer)
                    {
                        return;
                    }

                    var secondsLeft = RaidRecoveryApi.SecondsLeftOfResumedRaid();
                    var timer = e.Game?.GameTimer;
                    if (secondsLeft == null || timer == null)
                    {
                        return;
                    }

                    var original = timer.SessionTime;
                    timer.SessionTime = TimeSpan.FromSeconds(secondsLeft.Value);
                    Log.LogInfo($"Resumed raid duration: {timer.SessionTime:hh\\:mm\\:ss} instead of {original:hh\\:mm\\:ss}");
                    _hostIsToWarn = true;
                }
            );
        }

        private static void OnRaidStarted(FikaRaidStartedEvent e)
        {
            Guarded(
                "Raid started",
                () =>
                {
                    _joinedRaidRuns = !e.IsServer;
                    _leaveAt = 0f;
                    _hostIsToWarn = false;
                    if (_joinedRaidRuns && _doorsToApply != null)
                    {
                        var doors = _doorsToApply;
                        _doorsToApply = null;
                        ApplyDoors(doors);
                    }
                    Log.LogInfo($"Raid started, role: {(e.IsServer ? "host" : "client")}");
                }
            );
        }

        private static void OnGameEnded(FikaGameEndedEvent e)
        {
            Guarded(
                "Game ended",
                () =>
                {
                    _joinedRaidRuns = false;
                    _leaveAt = 0f;
                    _hostIsToWarn = false;
                    // A raid the player ends by themselves shows its screens
                    ExitScreenPatch.PassNext = false;
                    Log.LogInfo($"Raid over, role: {(e.IsServer ? "host" : "client")}, outcome: {e.ExitStatus}, exit: {e.ExitName ?? "none"}");
                    // Fika ends its raids without going through the function Raid Recovery watches
                    RaidRecoveryApi.RaidEnded();
                }
            );
        }

        private static void OnPeerConnected(PeerConnectedEvent e)
        {
            Guarded("Peer connected", () => Log.LogInfo($"Player connected, peer {e.Peer?.Id}, seen as {Role}"));
        }

        private static void OnPeerDisconnected(PeerDisconnectedEvent e)
        {
            Guarded("Peer disconnected", () => Log.LogInfo($"Player disconnected, peer {e.Peer?.Id}, seen as {Role}"));
        }

        /// <summary>
        /// The host of a resumed raid reads the warning where it looks: on the line above the "Start raid"
        /// button. A notification does not show on that screen. The button of Fika is what tells the
        /// moment: the line is written by Fika just before it creates it.
        /// </summary>
        private static void WarnTheHost()
        {
            if (!_hostIsToWarn || Time.unscaledTime < _lookAgainAt)
            {
                return;
            }

            _lookAgainAt = Time.unscaledTime + 0.5f;
            if (GameObject.Find("FikaStartButton") == null)
            {
                return;
            }

            _hostIsToWarn = false;
            if (!(Singleton<IFikaGame>.Instance is CoopGame game))
            {
                Log.LogWarning("The host could not be warned to wait for its players: no raid of Fika");
                return;
            }

            game.SetMatchmakerStatus(WaitForEveryoneShort);
            Notify(WaitForEveryone);
            Log.LogInfo("Host warned to wait for all its players before starting the raid");
        }

        /// <summary>
        /// When the host is gone, Fika leaves the player in a raid where nothing moves. A player who leaves
        /// the raid by themselves ends it first (<see cref="OnGameEnded"/>), so nothing is watched by then.
        /// </summary>
        private static void WatchTheHost()
        {
            if (!_joinedRaidRuns)
            {
                return;
            }

            if (_leaveAt > 0f)
            {
                if (Time.unscaledTime >= _leaveAt)
                {
                    _joinedRaidRuns = false;
                    _leaveAt = 0f;
                    LeaveWithoutEndingTheRaid();
                }

                return;
            }

            if (HostAnswers())
            {
                return;
            }

            Log.LogWarning($"The host of the raid is lost ({DescribeConnection()})");
            // The character stays as the last snapshot taken with the host has it: no snapshot is taken now,
            // it would be ahead of the map the host saved
            RaidRecoveryApi.RaidEnded();

            if (!LeaveWhenHostIsLost.Value)
            {
                _joinedRaidRuns = false;
                Notify("The host is lost. Leaving this raid by yourself ends it for good.");
                return;
            }

            _leaveAt = Time.unscaledTime + LeaveDelaySeconds;
            Notify("The host is lost. Back to the menu: you can rejoin the raid once the host has resumed it.");
        }

        /// <summary>
        /// Fika destroys its client only when the host timed out. A host whose game was closed is seen in
        /// other ways (connection closed, host unreachable): the client of Fika stays, with nobody at the
        /// other end. Both are read here.
        /// </summary>
        private static bool HostAnswers()
        {
            if (!Singleton<FikaClient>.Instantiated)
            {
                return false;
            }

            var net = Singleton<FikaClient>.Instance.NetClient;
            return net != null && net.ConnectedPeersCount > 0;
        }

        private static string DescribeConnection()
        {
            if (!Singleton<FikaClient>.Instantiated)
            {
                return "Fika destroyed its client: the host timed out";
            }

            var net = Singleton<FikaClient>.Instance.NetClient;
            return net == null ? "the client of Fika has no connection" : $"the client of Fika is there, {net.ConnectedPeersCount} host connected";
        }

        /// <summary>
        /// Takes the player out of the raid without telling the server the raid ended: for the server this
        /// player is still in it, as after a crash. This is the second place where the add-on reaches inside
        /// Fika: it makes Fika believe the character is already saved.
        /// </summary>
        private static void LeaveWithoutEndingTheRaid()
        {
            if (!(Singleton<IFikaGame>.Instance is CoopGame game))
            {
                Log.LogWarning("No raid of Fika to leave");
                return;
            }

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null)
            {
                Log.LogWarning("No player to take out of the raid");
                return;
            }

            var saved = AccessTools.Field(typeof(CoopGame), "_hasSaved");
            if (saved == null)
            {
                // Leaving anyway would end the raid on the server and erase the snapshot
                Log.LogError("Fika changed: the raid cannot be left without ending it. The player stays in the raid.");
                Notify("This version of Fika is not supported: leave the raid by yourself.");
                return;
            }

            saved.SetValue(game, true);
            Log.LogInfo($"Leaving the raid on {game.Location?.Id}, the server is not told it ended");
            // Not "Left": the player gave nothing up, and the game fails the quests that forbid leaving a
            // raid. "Runner" is the one outcome that counts neither as a raid survived nor as a raid lost.
            game.Stop(player.ProfileId, ExitStatus.Runner, null, 0f);
            // Set after Stop, which tells everyone the raid ended and so clears it (OnGameEnded)
            ExitScreenPatch.PassNext = true;
        }

        private static void Notify(string message)
        {
            try
            {
                NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Long, ENotificationIconType.Alert);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Notification failed: {ex.Message}");
            }
        }
    }
}
