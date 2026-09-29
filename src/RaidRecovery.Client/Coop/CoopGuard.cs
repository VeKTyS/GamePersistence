using BepInEx.Bootstrap;

namespace RaidRecovery.Client.Coop
{
    /// <summary>
    /// Tells the rest of the mod whether it stands down. Read from the list of plugins BepInEx loaded,
    /// without any reference to the co-op mod itself: the mod never depends on it.
    /// </summary>
    internal static class CoopGuard
    {
        private static bool? _stoodDown;

        /// <summary>
        /// True when a co-op mod is installed: nothing is captured and no raid is offered.
        /// Must not be read from Awake: BepInEx fills its list as it loads the plugins, the ones loaded
        /// after us would be missed. The menu and the raids come long after.
        /// </summary>
        public static bool IsStoodDown
        {
            get
            {
                if (_stoodDown.HasValue)
                {
                    return _stoodDown.Value;
                }

                var coopMod = CoopPolicy.CoopModAmong(Chainloader.PluginInfos.Keys);
                _stoodDown = coopMod != null;
                if (coopMod != null)
                {
                    Plugin.Log.LogWarning(
                        $"{coopMod} is installed: Raid Recovery stands down, raids are not saved and none is offered. Resuming a raid played with others needs the Fika add-on."
                    );
                }

                return _stoodDown.Value;
            }
        }
    }
}
