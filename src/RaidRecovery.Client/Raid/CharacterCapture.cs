using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using Newtonsoft.Json;
using UnityEngine;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// State of the character at a given moment. It is taken in two steps: Take reads the game and must run on
    /// the main thread; ToJson only touches copies and can run elsewhere.
    /// </summary>
    internal sealed class CharacterCapture
    {
        private CapturedProfile _profile;

        public Vector3 Position { get; private set; }

        public Vector2 Rotation { get; private set; }

        public static CharacterCapture Take(Player player)
        {
            return new CharacterCapture
            {
                Position = player.Position,
                Rotation = player.Rotation,
                _profile = new CapturedProfile
                {
                    // The same two calls as the game at the end of a raid (BaseLocalGame.GameEnd):
                    // this gives exactly the shape the server already knows how to read.
                    Inventory = new InventoryDescriptor(player.Profile.Inventory, FullySearchedSearchController.Instance),
                    Health = player.ActiveHealthController.Store(),
                    // Copy: the game's dictionary keeps changing while we serialize on another thread
                    Encyclopedia = new Dictionary<MongoID, bool>(player.Profile.Encyclopedia),
                    // Same conversions as the ProfileDescriptor constructor
                    Quests = player.Profile.QuestsData.ToList(),
                    TaskConditionCounters = player.Profile.TaskConditionCounters?.ToDictionary(
                        pair => pair.Key,
                        pair => new TaskConditionCounterDescriptor(pair.Value)
                    ),
                },
            };
        }

        /// <summary>Serializes with the game's converters, not with ours.</summary>
        public string ToJson()
        {
            return _profile.ToJson();
        }

        private sealed class CapturedProfile
        {
            [JsonProperty("Inventory")]
            public InventoryDescriptor Inventory;

            [JsonProperty("Health")]
            public Profile.HealthInfo Health;

            /// <summary>Items already examined: without them, the restored loot shows up as unknown again.</summary>
            [JsonProperty("Encyclopedia")]
            public Dictionary<MongoID, bool> Encyclopedia;

            [JsonProperty("Quests", NullValueHandling = NullValueHandling.Ignore)]
            public List<QuestDataClass> Quests;

            [JsonProperty("TaskConditionCounters", NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<MongoID, TaskConditionCounterDescriptor> TaskConditionCounters;
        }
    }
}
