#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRecovery.Client.Coop
{
    /// <summary>
    /// Whether the mod has to stand down because the raids are played with others.
    /// Knows nothing of the game, so it can be tested without it.
    /// </summary>
    internal static class CoopPolicy
    {
        public const string FikaId = "com.fika.core";

        /// <summary>
        /// The co-op mod found among the loaded plugins, or null. A snapshot taken in a raid played with others
        /// must never be offered as a raid to resume alone.
        /// </summary>
        public static string CoopModAmong(IEnumerable<string> pluginIds)
        {
            if (pluginIds == null)
            {
                return null;
            }

            return pluginIds.FirstOrDefault(id => string.Equals(id, FikaId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The mod does nothing at all: a co-op mod is there and nobody tells the mod what its raids are.
        /// </summary>
        public static bool StandsDown(string coopMod, bool coordinatorDeclared)
        {
            return coopMod != null && !coordinatorDeclared;
        }

        /// <summary>
        /// Whether the raid that starts is saved by this player. Alone, always. With others, only when the
        /// coordinator says so: the world of the raid belongs to one player, the others must not save it.
        /// </summary>
        public static bool SavesRaid(string coopMod, bool coordinatorDeclared, bool coordinatorSaves)
        {
            if (coopMod == null)
            {
                return true;
            }

            return coordinatorDeclared && coordinatorSaves;
        }
    }
}
