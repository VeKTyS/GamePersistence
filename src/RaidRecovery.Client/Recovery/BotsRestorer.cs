using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using RaidRecovery.Client.Models;
using UnityEngine;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// Puts back the bots that were alive at the snapshot, each with its gear, its health and its position.
    /// Goes through the game's own spawner, so the bots join its counters, its groups and its enemy lists.
    /// </summary>
    internal static class BotsRestorer
    {
        /// <summary>
        /// One bot after the other: each has to load its look and gear before the game can build it.
        /// Runs on the main thread, the waits hand the frame back to the game.
        /// </summary>
        /// <summary>Profiles of the bots put back in the raid in progress, to tell them from those the game spawned.</summary>
        public static readonly HashSet<string> RestoredIds = new HashSet<string>();

        public static async Task ApplyAsync(List<BotDto> bots)
        {
            RestoredIds.Clear();
            if (bots == null || bots.Count == 0)
            {
                return;
            }

            if (!Singleton<IBotGame>.Instantiated)
            {
                Plugin.Log.LogWarning("Bots not restored: this raid has no bots");
                return;
            }

            var spawner = Singleton<IBotGame>.Instance.BotsController?.BotSpawner;
            if (spawner == null)
            {
                Plugin.Log.LogWarning("Bots not restored: the game's spawner is not ready");
                return;
            }

            var spawned = 0;
            foreach (var bot in bots)
            {
                // One bot that fails must not cost the others
                try
                {
                    if (await SpawnAsync(spawner, bot))
                    {
                        spawned++;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Bot not restored: {ex}");
                }
            }

            Plugin.Log.LogInfo($"Bots restored: {spawned} handed to the game out of {bots.Count} recorded");
        }

        /// <summary>
        /// A bot that was after the player goes after them again. We only tell its group where the player is:
        /// the game decides the rest, as it does when a bot hears a shot.
        /// </summary>
        private static void Hunt(BotOwner owner)
        {
            try
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;
                var group = owner.BotsGroup;
                if (player == null || group == null || !group.Enemies.ContainsKey(player))
                {
                    return;
                }

                group.ReportAboutEnemy(player, EEnemyPartVisibleType.Sence, owner);
                if (owner.EnemiesController.EnemyInfos.TryGetValue(player, out var enemy))
                {
                    owner.Memory.GoalEnemy = enemy;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Bot {owner.Profile?.Nickname} does not remember the player: {ex.Message}");
            }
        }

        private static async Task<bool> SpawnAsync(BotSpawner spawner, BotDto bot)
        {
            if (bot?.Position == null || bot.Profile == null)
            {
                return false;
            }

            var position = new Vector3(bot.Position.X, bot.Position.Y, bot.Position.Z);
            var descriptor = bot.Profile.ToString().ParseJsonTo<ProfileDescriptor>();
            var profile = new Profile(descriptor);

            var zone = spawner.GetClosestZone(position, out _);
            // Navigation point the bot starts from: the closest one to where it stood
            var corePoint = AICorePointHolder.GetClosest(position);
            if (zone == null || corePoint == null)
            {
                Plugin.Log.LogWarning($"Bot {profile.Nickname} not restored: no zone around {position}");
                return false;
            }

            // Without this, the game builds a bot it cannot draw, which then fails on every frame
            await GameAssets.LoadAsync(profile.GetAllPrefabPaths(false));

            // The raid ended while the bot was loading
            if (!Singleton<IBotGame>.Instantiated || spawner._cancellationTokenSource.IsCancellationRequested)
            {
                return false;
            }

            // Never null: the game reads the spawn parameters of a boss without checking them
            var request = new GetProfileDataParams((EPlayerSide)bot.Side, (WildSpawnType)bot.Role, (BotDifficulty)bot.Difficulty, 0f, new BotSpawnParams(), false);
            var data = BotCreationData.CreateWithoutProfile(request);
            data.AddProfile(profile);
            data.AddPosition(position, corePoint.Id);

            var rotation = bot.Rotation;
            // Same three steps as the game's DebugSpawnAnyway: count the spawn, give the position, activate
            spawner._inSpawnProcess++;
            spawner.method_10(
                zone,
                data,
                owner =>
                {
                    if (owner == null || owner.GetPlayer == null)
                    {
                        return;
                    }

                    if (rotation != null)
                    {
                        owner.GetPlayer.Rotation = new Vector2(rotation.Yaw, rotation.Pitch);
                    }

                    RestoredIds.Add(owner.GetPlayer.ProfileId);
                    // Off by default: coming back to a raid with a bot already on your heels is no way to settle in
                    if (bot.HuntsPlayer && Plugin.BotsRememberPlayer.Value)
                    {
                        Hunt(owner);
                    }

                    Plugin.Log.LogInfo($"Bot back in the raid: {owner.Profile.Nickname} at {owner.GetPlayer.Position}");
                },
                spawner._cancellationTokenSource.Token
            );
            return true;
        }
    }
}
