#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRecovery.Client.Teammates
{
    /// <summary>
    /// What the mods that give the player AI teammates need from a resumed raid.
    /// Knows nothing of the game, so it can be tested without it.
    /// </summary>
    internal static class SquadPolicy
    {
        /// <summary>PIT Fireteam: brings back the squad picked in the menu, by the account ids of its teammates.</summary>
        public const string PitFireteamId = "xyz.pit.fireteam";

        /// <summary>Miyako Carry Service: brings back its squad by itself, for the side the menu last showed.</summary>
        public const string MiyakoId = "top.himesamanoyume.miyakocarryservice";

        public static bool IsAmong(IEnumerable<string> pluginIds, string id)
        {
            return pluginIds != null && pluginIds.Any(plugin => string.Equals(plugin, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The teammates to bring back: those of the mod's squad still alive next to the player, each once, in the
        /// order of the squad. A teammate that died stays dead. null when there is none.
        /// </summary>
        public static List<string> ToBringBack(IEnumerable<string> squad, IEnumerable<string> aliveFollowers)
        {
            if (squad == null || aliveFollowers == null)
            {
                return null;
            }

            var alive = new HashSet<string>(aliveFollowers.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            var kept = squad.Where(id => !string.IsNullOrEmpty(id) && alive.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
            return kept.Count == 0 ? null : kept;
        }

        /// <summary>Adds the teammates the list does not hold yet. Returns how many were added.</summary>
        public static int AddMissing(List<string> target, IEnumerable<string> squad)
        {
            if (target == null || squad == null)
            {
                return 0;
            }

            var added = 0;
            foreach (var id in squad)
            {
                if (!string.IsNullOrEmpty(id) && !target.Contains(id))
                {
                    target.Add(id);
                    added++;
                }
            }

            return added;
        }

        /// <summary>The squad waits for the raid of its map: any other raid launched meanwhile must not get it.</summary>
        public static bool IsRaidOf(string squadMap, string launchedMap)
        {
            return !string.IsNullOrEmpty(squadMap) && string.Equals(squadMap, launchedMap, StringComparison.OrdinalIgnoreCase);
        }
    }
}
