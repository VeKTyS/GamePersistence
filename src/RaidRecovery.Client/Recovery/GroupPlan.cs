#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// In which order the bots of a snapshot come back, and how many followers each leader gets.
    /// Knows nothing of the game, so it can be tested without it.
    /// </summary>
    internal static class GroupPlan
    {
        /// <summary>
        /// Leaders first: the game builds a group around the first bot it is given, and followers look for a
        /// leader that is already there. The order of the snapshot is kept otherwise.
        /// </summary>
        public static List<T> LeadersFirst<T>(IEnumerable<T> bots, System.Func<T, bool> isLeader)
        {
            // OrderBy is stable: two leaders, or two followers, keep the order they had
            return bots.Where(bot => bot != null).OrderBy(bot => isLeader(bot) ? 0 : 1).ToList();
        }

        /// <summary>
        /// Members of each group, leader included. A bot without a group is not counted: it comes back alone.
        /// </summary>
        public static Dictionary<int, int> Sizes<T>(IEnumerable<T> bots, System.Func<T, int?> groupOf)
        {
            return bots.Where(bot => bot != null && groupOf(bot).HasValue)
                .GroupBy(bot => groupOf(bot).Value)
                .ToDictionary(group => group.Key, group => group.Count());
        }

        /// <summary>
        /// Followers a leader may take: the members of its group still alive in the snapshot, itself left out.
        /// The ones that died are not waited for.
        /// </summary>
        public static int FollowersOf(IReadOnlyDictionary<int, int> sizes, int? group)
        {
            if (group.HasValue && sizes.TryGetValue(group.Value, out var size))
            {
                return System.Math.Max(0, size - 1);
            }

            return 0;
        }

        /// <summary>
        /// The bots that can come back now, in the order given: loaded, and not waiting for the leader of their
        /// group, still loading. A follower whose leader was given up on is no longer in the list: it comes back.
        /// </summary>
        public static List<T> ReadyNow<T>(IEnumerable<T> waiting, System.Func<T, bool> loaded, System.Func<T, bool> isLeader, System.Func<T, int?> groupOf)
        {
            var bots = waiting.Where(bot => bot != null).ToList();
            var leadersLoading = new HashSet<int>(
                bots.Where(bot => isLeader(bot) && groupOf(bot).HasValue && !loaded(bot)).Select(bot => groupOf(bot).Value)
            );
            return bots.Where(bot =>
                    loaded(bot) && (isLeader(bot) || !groupOf(bot).HasValue || !leadersLoading.Contains(groupOf(bot).Value))
                )
                .ToList();
        }

        /// <summary>true if the bot shares its group with at least one other bot of the snapshot.</summary>
        public static bool IsShared(IReadOnlyDictionary<int, int> sizes, int? group)
        {
            return group.HasValue && sizes.TryGetValue(group.Value, out var size) && size > 1;
        }
    }
}
