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
        public const string Guid = "com.oceane.raidrecovery";
        public const string Version = "0.3.1";

        public const int MinIntervalSeconds = 15;
        public const int MaxIntervalSeconds = 120;

        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> Enabled { get; private set; }

        internal static ConfigEntry<int> IntervalSeconds { get; private set; }

        internal static ConfigEntry<KeyboardShortcut> SaveNowKey { get; private set; }

        internal static ConfigEntry<bool> LogMeasurements { get; private set; }

        internal static ConfigEntry<bool> AutoLaunch { get; private set; }

        private void Awake()
        {
            Log = Logger;

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

            RecoveryController.Create(gameObject);

            new RaidStartedPatch().Enable();
            new RaidStoppedPatch().Enable();
            new MenuShownPatch().Enable();
            new SessionTimePatch().Enable();

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
