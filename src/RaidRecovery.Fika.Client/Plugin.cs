using System;
using BepInEx;
using BepInEx.Logging;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using RaidRecovery.Client.Api;

namespace RaidRecovery.Fika.Client
{
    /// <summary>
    /// Bridge between Raid Recovery and Fika. It saves and restores nothing by itself: it tells the base mod
    /// when to do it, and carries between the players what the base mod needs.
    /// </summary>
    [BepInPlugin(Guid, "Raid Recovery Fika", Version)]
    [BepInDependency(RaidRecovery.Client.Plugin.Guid, "1.1.0")]
    [BepInDependency("com.fika.core")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.vektys.raidrecovery.fika";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;

            // An exception here would leave Raid Recovery without a coordinator, and so stood down: nothing
            // is saved, nothing is offered. That is the safe side, we only make sure it is written.
            try
            {
                RaidRecoveryApi.Register(new FikaCoordinator());

                FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkCreated);
                FikaEventDispatcher.SubscribeEvent<AbstractGameCreatedEvent>(OnGameCreated);
                FikaEventDispatcher.SubscribeEvent<FikaRaidStartedEvent>(OnRaidStarted);
                FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(OnGameEnded);
                FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
                FikaEventDispatcher.SubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);

                Log.LogInfo($"Raid Recovery Fika {Version} loaded");
            }
            catch (Exception ex)
            {
                Log.LogError($"Raid Recovery Fika {Version} could not load, raids played with Fika are NOT saved: {ex}");
            }
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

        // The role is read at each raid, never kept: the same player hosts one raid and joins the next
        private static string Role => FikaBackendUtils.IsServer ? "host" : "client";

        private static void OnNetworkCreated(FikaNetworkManagerCreatedEvent e)
        {
            // The packets of the add-on are registered here, on each new manager. None exists yet
            Guarded("Network created", () => Log.LogInfo($"Network ready, role: {Role}"));
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
                }
            );
        }

        private static void OnRaidStarted(FikaRaidStartedEvent e)
        {
            Guarded("Raid started", () => Log.LogInfo($"Raid started, role: {(e.IsServer ? "host" : "client")}"));
        }

        private static void OnGameEnded(FikaGameEndedEvent e)
        {
            Guarded(
                "Game ended",
                () =>
                {
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
    }
}
