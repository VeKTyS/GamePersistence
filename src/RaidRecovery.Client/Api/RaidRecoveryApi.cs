using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaidRecovery.Client.Coop;
using RaidRecovery.Client.Net;
using RaidRecovery.Client.Raid;
using RaidRecovery.Client.Recovery;
using SPT.Reflection.Utils;

namespace RaidRecovery.Client.Api
{
    /// <summary>
    /// The only door of Raid Recovery for another mod. It gives access to what the mod already does, it
    /// holds no rule of its own.
    /// </summary>
    public static class RaidRecoveryApi
    {
        /// <summary>
        /// A snapshot starts being read, on the main thread. The players of one raid must be read at the
        /// same moment, or an item handed from one to the other would be saved twice, or not at all.
        /// </summary>
        public static event Action SnapshotStarted;

        /// <summary>
        /// Doors and switches were put back as the snapshot has them, on this machine only. Given: the ones
        /// that had to change, by identifier. The other players of the raid do not see it unless told.
        /// </summary>
        public static event Action<IReadOnlyDictionary<string, byte>> DoorsRestored;

        /// <summary>
        /// Declares the mod that speaks for the co-op mod. Without one, Raid Recovery stands down as soon as
        /// a co-op mod is installed.
        /// </summary>
        public static void Register(ICoopCoordinator coordinator)
        {
            if (coordinator == null)
            {
                throw new ArgumentNullException(nameof(coordinator));
            }

            CoopGuard.Coordinator = coordinator;
            Plugin.Log.LogInfo($"Coordinator declared: {coordinator.Name}");
        }

        /// <summary>Asks for a snapshot on the next frame. Does nothing out of a raid that is saved.</summary>
        public static void RequestSnapshot()
        {
            SnapshotCapturer.Current?.RequestCapture();
        }

        /// <summary>
        /// The raid is over, whatever the outcome. Raid Recovery sees it by itself when the raid is the
        /// game's own; a co-op mod ends its raids its own way and has to say it. The snapshot is left as
        /// it is: the server erases it when the game tells it the raid ended.
        /// </summary>
        public static void RaidEnded()
        {
            SnapshotCapturer.Current?.Stop();
            RecoveryController.Instance?.CheckOnNextMenu();
        }

        /// <summary>
        /// Time to give to the raid being created, or null when it is not the raid of a recovery. Read from
        /// the map the game is about to load.
        /// </summary>
        public static int? SecondsLeftOfResumedRaid()
        {
            var map = ClientAppUtils.GetMainApp()?._raidSettings?.SelectedLocation?.Id;
            return RecoveryController.Instance?.SecondsLeftFor(map);
        }

        /// <summary>When that player last resumed a raid, by the clock of the server, or null.</summary>
        public static async Task<DateTimeOffset?> LastResumeOfAsync(string profileId)
        {
            var result = await RecoveryApi.GetResumedAsync(profileId).ConfigureAwait(false);
            return result?.ResumedAt;
        }

        /// <summary>
        /// Puts doors and switches in the given states, the way a recovery does. To call on the main
        /// thread, in a raid. Returns how many had to change.
        /// </summary>
        public static int ApplyDoors(IReadOnlyDictionary<string, byte> states)
        {
            if (states == null || states.Count == 0)
            {
                return 0;
            }

            return WorldRestorer.ApplyObjects(states).Count;
        }

        internal static void RaiseDoorsRestored(IReadOnlyDictionary<string, byte> changed)
        {
            try
            {
                DoorsRestored?.Invoke(changed);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"A listener of the restored doors failed: {ex}");
            }
        }

        internal static void RaiseSnapshotStarted()
        {
            try
            {
                SnapshotStarted?.Invoke();
            }
            catch (Exception ex)
            {
                // A listener is code of another mod: its failure must not cost the snapshot
                Plugin.Log.LogError($"A listener of the snapshot failed: {ex}");
            }
        }
    }
}
