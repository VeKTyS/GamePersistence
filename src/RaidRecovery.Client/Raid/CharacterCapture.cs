using EFT;
using UnityEngine;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// State of the character at a given moment. The two Read methods take the game's state and must run on
    /// the main thread; ToJson only touches copies and can run elsewhere.
    /// </summary>
    internal sealed class CharacterCapture
    {
        private ProfileDescriptor _profile;

        public Vector3 Position { get; private set; }

        public Vector2 Rotation { get; private set; }

        /// <summary>
        /// Statistics of the raid so far, already as JSON: their lists are the ones the game keeps writing to,
        /// so they are serialized on the main thread, and not later on another one.
        /// </summary>
        public string StatsJson { get; private set; }

        public void ReadStats(Player player)
        {
            StatsJson = player.Profile.Stats == null ? null : new ProfileStatsSeparatorDescriptor(player.Profile.Stats).ToJson();
        }

        public void ReadProfile(Player player)
        {
            // The whole profile, built the way the game does it at the end of a raid (BaseLocalGame.GameEnd):
            // skills, quests, achievements, examined items, traders... This is exactly the shape the server
            // already knows how to read, and nothing of the raid is left aside.
            var profile = new ProfileDescriptor(player.Profile, FullySearchedSearchController.Instance);
            // The profile holds the health of the start of the raid, the controller holds the current one
            profile.Health = player.ActiveHealthController.Store();
            // Sent apart, see StatsJson: left here, they would be serialized on another thread while the game writes to them
            profile.Stats = new ProfileStatsSeparatorDescriptor();

            _profile = profile;
            // Read last, in the same step as the gear: the position of the snapshot is the one of its inventory
            Position = player.Position;
            Rotation = player.Rotation;
        }

        /// <summary>Serializes with the game's converters, not with ours.</summary>
        public string ToJson()
        {
            return _profile.ToJson();
        }
    }
}
