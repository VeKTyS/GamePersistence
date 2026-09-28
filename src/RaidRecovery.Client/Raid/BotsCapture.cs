using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using JsonType;
using Newtonsoft.Json.Linq;
using RaidRecovery.Client.Models;
using UnityEngine;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// Bots alive and bodies on the map at a given moment. Same two steps as the character: Take copies
    /// what it needs on the main thread, ToDtos turns the copies into JSON and can run elsewhere.
    /// </summary>
    internal sealed class BotsCapture
    {
        private readonly List<Bot> _bots = new List<Bot>();
        private readonly List<LootItemSerializer> _corpses = new List<LootItemSerializer>();

        public int BotCount => _bots.Count;

        public int CorpseCount => _corpses.Count;

        public static BotsCapture Take(GameWorld gameWorld)
        {
            var capture = new BotsCapture();

            foreach (var player in gameWorld.AllAlivePlayersList)
            {
                // One bot that cannot be read must not cost the others
                try
                {
                    if (player != null && player.IsAI && !player.IsYourPlayer && player.HealthController != null && player.HealthController.IsAlive)
                    {
                        capture._bots.Add(Describe(player));
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Bot skipped in the snapshot: {ex.Message}");
                }
            }

            foreach (var corpse in gameWorld.LootList.OfType<Corpse>())
            {
                try
                {
                    if (corpse != null && corpse.Item != null)
                    {
                        capture._corpses.Add(Describe(corpse));
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Body skipped in the snapshot: {ex.Message}");
                }
            }

            return capture;
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
            return new Bot
            {
                Position = player.Position,
                Rotation = player.Rotation,
                Side = (int)player.Profile.Side,
                Role = (int)settings.Role,
                Difficulty = (int)settings.BotDifficulty,
                Descriptor = descriptor,
            };
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
            public ProfileDescriptor Descriptor;
        }
    }
}
