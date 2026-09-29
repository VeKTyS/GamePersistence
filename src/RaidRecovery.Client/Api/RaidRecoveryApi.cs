using System;
using RaidRecovery.Client.Coop;
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

        /// <summary>
        /// The raid is over, whatever the outcome. Raid Recovery sees it by itself when the raid is the
        /// game's own; a co-op mod ends its raids its own way and has to say it.
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
    }
}
