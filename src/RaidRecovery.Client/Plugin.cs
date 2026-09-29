using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RaidRecovery.Client.Patches;
using RaidRecovery.Client.Raid;
using RaidRecovery.Client.Recovery;
using UnityEngine;

namespace RaidRecovery.Client
{
    [BepInPlugin(Guid, "Raid Recovery", Version)]
    [BepInDependency("com.SPT.core", "4.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.vektys.raidrecovery";
        public const string Version = "1.1.0";

        public const int MinIntervalSeconds = 15;
        public const int MaxIntervalSeconds = 120;

        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> Enabled { get; private set; }

        internal static ConfigEntry<int> IntervalSeconds { get; private set; }

        internal static ConfigEntry<KeyboardShortcut> SaveNowKey { get; private set; }

        internal static ConfigEntry<bool> LogMeasurements { get; private set; }

        internal static ConfigEntry<bool> AutoLaunch { get; private set; }

        internal static ConfigEntry<bool> KeepBots { get; private set; }

        internal static ConfigEntry<bool> KeepLoot { get; private set; }

        internal static ConfigEntry<bool> BotsRememberPlayer { get; private set; }

        internal static ConfigEntry<bool> GameScreen { get; private set; }

        private void Awake()
        {
            Log = Logger;

            // An exception here goes to the log of Unity, which nobody reads, and the mod is then silently absent:
            // no capture, no recovery. We write it where the other lines of the mod are.
            try
            {
                Load();
            }
            catch (Exception ex)
            {
                Log.LogError($"Raid Recovery {Version} could not load, raids are NOT saved: {ex}");
            }
        }

        private void Load()
        {

            Enabled = Config.Bind("Capture", "Enabled", true, "Saves the raid state so it can be resumed after a crash.");
            IntervalSeconds = Config.Bind(
                "Capture",
                "Interval (seconds)",
                30,
                new ConfigDescription(
                    "Time between two snapshots.",
                    new AcceptableValueRange<int>(MinIntervalSeconds, MaxIntervalSeconds)
                )
            );
            SaveNowKey = Config.Bind(
                "Capture",
                "Save now (shortcut)",
                KeyboardShortcut.Empty,
                "Takes a snapshot right away, without waiting for the next one. Only works during a raid."
            );
            // Holds no value: the entry only exists to show a button in the configuration menu
            Config.Bind(
                "Capture",
                "Save now",
                "",
                new ConfigDescription(
                    "Takes a snapshot right away, without waiting for the next one. Only works during a raid.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawSaveNow,
                        HideDefaultButton = true,
                        Order = -1,
                    }
                )
            );

            LogMeasurements = Config.Bind(
                "Diagnostics",
                "Log measurements",
                true,
                "Writes to the log the cost of each capture (ms) and the size of the snapshot (bytes)."
            );

            AutoLaunch = Config.Bind(
                "Recovery",
                "Relaunch the raid automatically",
                true,
                "After \"Resume\", relaunches the raid on the same map. Otherwise, the raid has to be started by hand."
            );

            GameScreen = Config.Bind(
                "Recovery",
                // BepInEx refuses = \ " ' [ ] in a setting name, and the whole plugin fails to load with it
                "Use the return-to-raid screen of the game",
                true,
                "Offers the recovery on the screen the live game shows after a disconnection. When off, or if that screen cannot be opened, a plain window is used."
            );

            BotsRememberPlayer = Config.Bind(
                "Recovery",
                "Bots that were after you still are",
                true,
                "When on, a bot that was chasing you when the raid was cut chases you again as soon as it is back. When off, it comes back unaware of you, which gives you time to settle in."
            );

            KeepLoot = Config.Bind(
                "Recovery",
                "Keep the loot as the game sees it",
                true,
                "Records every loot item still on the map and the items dropped or moved. When off, only the items carried by the player are removed from the loot of a resumed raid."
            );

            KeepBots = Config.Bind(
                "Recovery",
                "Keep bots and bodies",
                true,
                "Saves the bots alive and the bodies on the map, and puts them back when the raid is resumed. When off, the resumed raid spawns new bots."
            );

            RecoveryController.Create(gameObject);

            new RaidStartedPatch().Enable();
            new RaidStoppedPatch().Enable();
            new MenuShownPatch().Enable();
            new SessionTimePatch().Enable();
            new CorpseAssetsPatch().Enable();

            Log.LogInfo($"Raid Recovery {Version} loaded");
        }

        private static void DrawSaveNow(ConfigEntryBase entry)
        {
            var capturer = SnapshotCapturer.Current;
            if (capturer == null)
            {
                GUILayout.Label("Not in a raid", GUILayout.ExpandWidth(true));
                return;
            }

            if (GUILayout.Button("Save now", GUILayout.ExpandWidth(true)))
            {
                capturer.RequestCapture();
            }

            GUILayout.Label(capturer.LastSaveStatus, GUILayout.ExpandWidth(false));
        }
    }
}
