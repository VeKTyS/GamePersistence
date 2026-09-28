using System;
using System.Linq;
using EFT;
using EFT.Counters;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// Kills, experience and counters of the interrupted raid. The game starts every raid with empty session
    /// statistics: we add those of the snapshot to them, so the end-of-raid screen counts the whole raid.
    /// </summary>
    internal static class StatsRestorer
    {
        public static void Apply(Player player, string statsJson)
        {
            if (string.IsNullOrEmpty(statsJson))
            {
                return;
            }

            var descriptor = statsJson.ParseJsonTo<ProfileStatsSeparatorDescriptor>();
            if (descriptor?.Eft == null || player.Profile?.Stats?.Eft == null)
            {
                return;
            }

            // The game's own reader, so the counters come back in the shape it uses
            var saved = new ProfileStats(descriptor.Eft);
            var live = player.Profile.Stats.Eft;

            // We fill the objects the game already holds rather than swap them: it may keep a reference to them
            var counters = MergeCounters(saved.SessionCounters, live.SessionCounters);

            var victims = 0;
            if (saved.Victims != null && live.Victims != null)
            {
                foreach (var victim in saved.Victims)
                {
                    live.Victims.Add(victim);
                    victims++;
                }
            }

            live.TotalSessionExperience += saved.TotalSessionExperience;

            if (saved.DroppedItems != null && live.DroppedItems != null)
            {
                live.DroppedItems.AddRange(saved.DroppedItems);
            }

            if (saved.FoundInRaidItems != null && live.FoundInRaidItems != null)
            {
                live.FoundInRaidItems.AddRange(saved.FoundInRaidItems);
            }

            Plugin.Log.LogInfo(
                $"Raid statistics restored: {victims} kills, {saved.TotalSessionExperience} experience, {counters} counters"
            );
        }

        private static int MergeCounters(CountersCollection saved, CountersCollection live)
        {
            if (saved?._counters == null || live?._counters == null)
            {
                return 0;
            }

            foreach (var counter in saved._counters)
            {
                live._counters.TryGetValue(counter.Key, out var current);
                // A record (longest shot, longest streak) is the best of the two halves, anything else adds up
                var isRecord = CountersCollection._countersToMax.Any(tag => counter.Key.Contains(tag));
                live._counters[counter.Key] = isRecord ? Math.Max(current, counter.Value) : current + counter.Value;
            }

            return saved._counters.Count;
        }
    }
}
