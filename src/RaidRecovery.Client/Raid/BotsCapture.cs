using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using JsonType;
using Newtonsoft.Json.Linq;
using RaidRecovery.Client.Models;
using RaidRecovery.Client.Teammates;
using UnityEngine;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// Bots alive and bodies on the map. Same two steps as the character: Read copies what it needs on the
    /// main thread, one bot or one body at a time; ToDtos turns the copies into JSON and can run elsewhere.
    /// </summary>
    internal sealed class BotsCapture
    {
        private readonly List<Bot> _bots = new List<Bot>();
        private readonly List<LootItemSerializer> _corpses = new List<LootItemSerializer>();

        // Bodies already read, so none is written twice
        private readonly HashSet<string> _bodies = new HashSet<string>();

        public int BotCount => _bots.Count;

        public int CorpseCount => _corpses.Count;

        /// <summary>Bots to read, as they are when the pass starts. Each is then read in a step of its own.</summary>
        public static List<Player> BotsOf(GameWorld gameWorld)
        {
            return gameWorld
                .AllAlivePlayersList.Where(player => player != null && player.IsAI && !player.IsYourPlayer && !TeammateMods.FollowsAPlayer(player))
                .ToList();
        }

        public static List<Corpse> CorpsesOf(GameWorld gameWorld)
        {
            return gameWorld.LootList.OfType<Corpse>().Where(corpse => corpse != null && corpse.Item != null).ToList();
        }

        /// <summary>
        /// Several frames go by between the list and this read. A bot killed in between is read as the body it
        /// became: otherwise it would be in the snapshot neither alive nor dead.
        /// </summary>
        public void Read(Player bot, GameWorld gameWorld)
        {
            if (bot == null)
            {
                return;
            }

            if (bot.HealthController != null && bot.HealthController.IsAlive)
            {
                _bots.Add(Describe(bot));
                return;
            }

            var profileId = bot.ProfileId;
            var body = gameWorld.LootList.OfType<Corpse>().FirstOrDefault(corpse => corpse != null && corpse.PlayerProfileID == profileId);
            if (body != null && body.Item != null && _bodies.Add(body.Item.Id))
            {
                _corpses.Add(Describe(body));
            }
        }

        public void Read(Corpse corpse)
        {
            // Destroyed by the game since the list was made
            if (corpse == null || corpse.Item == null)
            {
                return;
            }

            if (_bodies.Add(corpse.Item.Id))
            {
                _corpses.Add(Describe(corpse));
            }
        }

        private static Bot Describe(Player player)
        {
            // The descriptor the game builds to send a profile: what Profile(ProfileDescriptor) reads back
            var descriptor = new ProfileDescriptor(player.Profile, FullySearchedSearchController.Instance);
            // The profile holds the health of the start of the raid, the controller holds the current one
            if (player.ActiveHealthController != null)
            {
                descriptor.Health = player.ActiveHealthController.Store();
            }

            var settings = player.Profile.Info.Settings;
            var owner = player.AIData?.BotOwner;
            var group = owner?.BotsGroup;
            return new Bot
            {
                Group = group?.Id,
                Zone = group?.BotZone?.name,
                IsBoss = owner?.Boss != null && owner.Boss.IamBoss,
                Position = player.Position,
                Rotation = player.Rotation,
                Side = (int)player.Profile.Side,
                Role = (int)settings.Role,
                Difficulty = (int)settings.BotDifficulty,
                HuntsPlayer = HuntsPlayer(player),
                Descriptor = descriptor,
            };
        }

        private static bool HuntsPlayer(Player bot)
        {
            var target = bot.AIData?.BotOwner?.Memory?.GoalEnemy?.Person;
            return target != null && target.IsYourPlayer;
        }

        private static LootItemSerializer Describe(Corpse corpse)
        {
            var body = new JsonCorpse
            {
                Id = "RaidRecovery_" + corpse.Item.Id,
                Position = corpse.transform.position,
                Rotation = corpse.transform.rotation.eulerAngles,
                Item = corpse.Item,
                Customization = corpse.Customization,
                Side = corpse.Side,
                // Read from the body as it lies now, not from the pose cached when it fell
                Bones = corpse.GetTransformSync(),
                ProfileID = corpse.PlayerProfileID,
                IsZombieCorpse = corpse.IsZombieCorpse,
            };

            // The game's own writer for the loot of a raid: the format it reads back at the next raid start
            var serializer = new LootItemSerializer();
            ((ISerializer<JsonCorpse>)serializer).Serialize(body);
            // The game's writer forgets this field
            serializer.IsZombieCorpse = body.IsZombieCorpse;
            return serializer;
        }

        public List<BotDto> BotsToDtos()
        {
            return _bots
                .Select(bot => new BotDto
                {
                    Position = new PositionDto { X = bot.Position.x, Y = bot.Position.y, Z = bot.Position.z },
                    Rotation = new RotationDto { Yaw = bot.Rotation.x, Pitch = bot.Rotation.y },
                    Side = bot.Side,
                    Role = bot.Role,
                    Difficulty = bot.Difficulty,
                    HuntsPlayer = bot.HuntsPlayer,
                    Group = bot.Group,
                    Zone = bot.Zone,
                    IsBoss = bot.IsBoss,
                    Profile = new JRaw(bot.Descriptor.ToJson()),
                })
                .ToList();
        }

        public List<JRaw> CorpsesToJson()
        {
            return _corpses.Select(corpse => new JRaw(corpse.ToJson())).ToList();
        }

        private sealed class Bot
        {
            public Vector3 Position;
            public Vector2 Rotation;
            public int Side;
            public int Role;
            public int Difficulty;
            public bool HuntsPlayer;
            public int? Group;
            public string Zone;
            public bool IsBoss;
            public ProfileDescriptor Descriptor;
        }
    }
}
