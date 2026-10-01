using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
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
        /// Longest wait, once the player is back in place, for the bots still loading. A load that never ends
        /// costs only its own bot: the others come back as soon as they are ready.
        /// </summary>
        private const int LoadTimeoutSeconds = 30;

        /// <summary>Profiles of the bots put back in the raid in progress, to tell them from those the game spawned.</summary>
        public static readonly HashSet<string> RestoredIds = new HashSet<string>();

        // The bots being prepared for the raid that is loading, and the snapshot list they come from
        private static List<PreparedBot> _prepared = new List<PreparedBot>();
        private static List<BotDto> _preparedFrom;

        /// <summary>
        /// Starts loading the look and gear of every bot while the raid is still loading, as the game does for
        /// its own first bots. Each bot loads on its own: one that never ends holds back no other.
        /// </summary>
        public static void Preload(List<BotDto> bots)
        {
            _prepared = new List<PreparedBot>();
            _preparedFrom = bots;
            if (bots == null || bots.Count == 0)
            {
                return;
            }

            foreach (var bot in bots)
            {
                // One bot that fails must not cost the others
                try
                {
                    if (bot?.Position == null || bot.Profile == null)
                    {
                        continue;
                    }

                    var profile = new Profile(bot.Profile.ToString().ParseJsonTo<ProfileDescriptor>());
                    // Without this, the game builds a bot it cannot draw, which then fails on every frame
                    var load = GameAssets.LoadAsync(profile.GetAllPrefabPaths(false), JobYieldPriority.Immediate);
                    _prepared.Add(new PreparedBot(bot, profile, load));
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Bot not restored, its profile cannot be read: {ex}");
                }
            }

            Plugin.Log.LogInfo($"Loading {_prepared.Count} bots while the raid loads");
        }

        /// <summary>
        /// Once the player is back in place: each bot comes back as soon as it is loaded, leaders before their
        /// guards, one per frame so that the game does not stall. Runs on the main thread.
        /// </summary>
        public static async Task ApplyAsync(List<BotDto> bots)
        {
            RestoredIds.Clear();
            if (bots == null || bots.Count == 0)
            {
                return;
            }

            // Not prepared during the loading, or prepared for another list: loaded now
            if (!ReferenceEquals(_preparedFrom, bots))
            {
                Preload(bots);
            }

            var prepared = _prepared;
            _prepared = new List<PreparedBot>();
            _preparedFrom = null;

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

            var sizes = GroupPlan.Sizes(bots, bot => bot.Group);
            // Zone each group of the snapshot comes back in: its members have to share it to find each other
            var zones = new Dictionary<int, BotZone>();
            var waiting = GroupPlan.LeadersFirst(prepared, entry => entry.Bot.IsBoss);
            var ready = prepared.Count(entry => entry.Load.IsCompleted);
            Plugin.Log.LogInfo($"Putting back {waiting.Count} bots, {ready} already loaded");

            var deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            var spawned = 0;
            while (waiting.Count > 0)
            {
                // The raid ended while the bots were coming back
                if (!Singleton<IBotGame>.Instantiated || spawner._cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    foreach (var late in waiting.Where(entry => !entry.Load.IsCompleted).ToList())
                    {
                        Plugin.Log.LogWarning(
                            $"Bot {late.Profile.Nickname} ({(WildSpawnType)late.Bot.Role}) not restored: its look and gear were still loading after {LoadTimeoutSeconds} s"
                        );
                        waiting.Remove(late);
                    }
                }

                var now = GroupPlan.ReadyNow(waiting, entry => entry.Load.IsCompleted, entry => entry.Bot.IsBoss, entry => entry.Bot.Group);
                if (now.Count == 0)
                {
                    var left = Math.Max(0f, deadline - Time.realtimeSinceStartup);
                    var loads = waiting.Where(entry => !entry.Load.IsCompleted).Select(entry => entry.Load).ToList();
                    loads.Add(Task.Delay(TimeSpan.FromSeconds(left)));
                    await Task.WhenAny(loads);
                    continue;
                }

                foreach (var entry in now)
                {
                    waiting.Remove(entry);
                    // One bot that fails must not cost the others
                    try
                    {
                        if (entry.Load.IsFaulted)
                        {
                            Plugin.Log.LogError(
                                $"Bot {entry.Profile.Nickname} not restored, its look and gear failed to load: {entry.Load.Exception?.GetBaseException()}"
                            );
                            continue;
                        }

                        if (Spawn(spawner, entry, sizes, zones))
                        {
                            spawned++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"Bot not restored: {ex}");
                    }

                    // One bot per frame: building a bot costs the game a moment
                    await Task.Yield();
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

        /// <summary>
        /// The game puts a bot in the group of its zone. Bots that shared a group get the zone of that group,
        /// by its name; alone, a bot gets the zone closest to where it stood, as before.
        /// </summary>
        private static BotZone ZoneOf(BotSpawner spawner, BotDto bot, Vector3 position, Dictionary<int, int> sizes, Dictionary<int, BotZone> zones)
        {
            if (!GroupPlan.IsShared(sizes, bot.Group))
            {
                return spawner.GetClosestZone(position, out _);
            }

            if (zones.TryGetValue(bot.Group.Value, out var known))
            {
                return known;
            }

            var zone = string.IsNullOrEmpty(bot.Zone) ? null : spawner.GetZoneByName(bot.Zone);
            if (zone == null)
            {
                zone = spawner.GetClosestZone(position, out _);
            }

            if (zone != null)
            {
                zones[bot.Group.Value] = zone;
            }

            return zone;
        }

        /// <summary>
        /// The game only runs the logic of a boss, and only lets followers join it, once it was told the bot
        /// leads. It then takes the members of its group by itself, now and every 15 seconds.
        /// </summary>
        private static void Lead(BotOwner owner, int followers)
        {
            try
            {
                owner.Boss.SetBoss(followers);
                Plugin.Log.LogInfo($"Bot {owner.Profile?.Nickname} leads its group again, {followers} followers expected");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Bot {owner.Profile?.Nickname} does not lead its group: {ex.Message}");
            }
        }

        private static bool Spawn(BotSpawner spawner, PreparedBot entry, Dictionary<int, int> sizes, Dictionary<int, BotZone> zones)
        {
            var bot = entry.Bot;
            var profile = entry.Profile;
            var position = new Vector3(bot.Position.X, bot.Position.Y, bot.Position.Z);

            var zone = ZoneOf(spawner, bot, position, sizes, zones);
            var followers = GroupPlan.FollowersOf(sizes, bot.Group);
            // Navigation point the bot starts from: the closest one to where it stood
            var corePoint = AICorePointHolder.GetClosest(position);
            if (zone == null || corePoint == null)
            {
                Plugin.Log.LogWarning($"Bot {profile.Nickname} not restored: no zone around {position}");
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
                    if (bot.IsBoss)
                    {
                        Lead(owner, followers);
                    }

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

        /// <summary>A bot of the snapshot, its profile read once and its look and gear loading.</summary>
        private sealed class PreparedBot
        {
            public PreparedBot(BotDto bot, Profile profile, Task load)
            {
                Bot = bot;
                Profile = profile;
                Load = load;
            }

            public BotDto Bot { get; }

            public Profile Profile { get; }

            public Task Load { get; }
        }
    }
}
