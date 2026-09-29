using System;
using BepInEx.Bootstrap;
using RaidRecovery.Client.Api;

namespace RaidRecovery.Client.Coop
{
    /// <summary>
    /// Tells the rest of the mod what it may do when the raids are played with others. Read from the list of
    /// plugins BepInEx loaded, without any reference to the co-op mod itself: the mod never depends on it.
    /// </summary>
    internal static class CoopGuard
    {
        private static bool _detected;
        private static string _coopMod;

        /// <summary>The mod that speaks for the co-op mod, or null. Set once, by that mod, when it loads.</summary>
        public static ICoopCoordinator Coordinator { get; set; }

        /// <summary>
        /// True when a co-op mod is installed and nothing speaks for it: nothing is captured, no raid is offered.
        /// </summary>
        public static bool IsStoodDown => CoopPolicy.StandsDown(CoopMod, Coordinator != null);

        /// <summary>What this player saves of the raid that starts.</summary>
        public static CaptureScope ScopeOfThisRaid
        {
            get
            {
                // Alone, the coordinator has no say: a raid of the game is saved whole
                if (CoopMod == null)
                {
                    return CaptureScope.Everything;
                }

                var coordinator = Coordinator;
                var asked = coordinator == null ? CaptureScope.Nothing : Asks(coordinator);
                return CoopPolicy.SavesRaid(CoopMod, coordinator != null, asked != CaptureScope.Nothing) ? asked : CaptureScope.Nothing;
            }
        }

        /// <summary>Who this player is in the raid in progress, or null alone.</summary>
        public static string DescribeThisRaid()
        {
            var coordinator = Coordinator;
            if (coordinator == null || CoopMod == null)
            {
                return null;
            }

            try
            {
                return coordinator.DescribeThisRaid();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{coordinator.Name} could not describe this raid: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Must not be read from Awake: BepInEx fills its list as it loads the plugins, the ones loaded
        /// after us would be missed. The menu and the raids come long after.
        /// </summary>
        private static string CoopMod
        {
            get
            {
                if (_detected)
                {
                    return _coopMod;
                }

                _coopMod = CoopPolicy.CoopModAmong(Chainloader.PluginInfos.Keys);
                _detected = true;
                if (_coopMod == null)
                {
                    return null;
                }

                if (Coordinator == null)
                {
                    Plugin.Log.LogWarning(
                        $"{_coopMod} is installed: Raid Recovery stands down, raids are not saved and none is offered. Resuming a raid played with others needs the Fika add-on."
                    );
                }
                else
                {
                    Plugin.Log.LogInfo($"{_coopMod} is installed: {Coordinator.Name} tells Raid Recovery which raids to save");
                }

                return _coopMod;
            }
        }

        /// <summary>The coordinator is code of another mod: its failure must not cost the start of the raid.</summary>
        private static CaptureScope Asks(ICoopCoordinator coordinator)
        {
            try
            {
                return coordinator.ScopeOfThisRaid;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{coordinator.Name} could not say whether this raid is saved, it is not: {ex}");
                return CaptureScope.Nothing;
            }
        }
    }
}
