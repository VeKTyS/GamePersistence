using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;

namespace RaidRecovery.Client.Teammates
{
    /// <summary>
    /// The mods that give the player AI teammates. Each brings its squad back at the start of a raid, so the mod
    /// never puts a teammate back itself: it only gives each mod what a resumed raid skips. Read from the list of
    /// plugins BepInEx loaded, without any reference to those mods: nothing of this runs when they are absent.
    /// </summary>
    internal static class TeammateMods
    {
        private const string PitSquadHolder = "pitTeam.Utils.SpawnHelper";
        private const string MiyakoSideHolder = "MiyakoCarryService.Client.Patches.Events.MatchmakerAcceptScreenShowPatch";

        private static bool _detected;

        // PIT Fireteam's own lists of the squad of the raid, PMC and scav. null: PIT absent or unreadable
        private static List<string> _pitSquad;
        private static List<string> _pitScavSquad;

        // The side Miyako Carry Service brings its squad for. null: Miyako absent or unreadable
        private static FieldInfo _miyakoSide;

        /// <summary>
        /// A bot that follows a human player. Every player has an AIBossPlayer, bots included, and a guard that
        /// lost its boss may follow the AIBossPlayer of a boss bot: only a human leader makes it a teammate.
        /// Only teammate mods make a bot follow a human. Their bots are not saved: the mod brings them back.
        /// </summary>
        public static bool FollowsAPlayer(Player bot)
        {
            return bot.AIData?.BotOwner?.BotFollower?.BossToFollow is AIBossPlayer leader && leader._player != null && !leader._player.IsAI;
        }

        /// <summary>
        /// The squad of the player to bring back at the resume, by the ids its mod knows. Read on the main thread.
        /// null when no mod that needs it is installed.
        /// </summary>
        public static List<string> SquadOf(GameWorld world, Player player)
        {
            Detect();
            if (_pitSquad == null || world == null || player == null)
            {
                return null;
            }

            var followers = world
                .AllAlivePlayersList.Where(bot =>
                    bot != null
                    && bot.IsAI
                    && bot.AIData?.BotOwner?.BotFollower?.BossToFollow is AIBossPlayer leader
                    && leader._player == player
                )
                .Select(bot => bot.Profile?.AccountId);
            return SquadPolicy.ToBringBack(_pitSquad.Concat(_pitScavSquad ?? Enumerable.Empty<string>()), followers);
        }

        /// <summary>
        /// Called just before the mod launches the resumed raid itself. Going through the menu, the teammate
        /// mods read the squad and the side on screens this launch skips: each gets them here instead.
        /// </summary>
        public static void BeforeLaunch(string map, bool scav, List<string> squad)
        {
            Detect();

            if (_miyakoSide != null)
            {
                try
                {
                    _miyakoSide.SetValue(null, scav ? ESideType.Savage : ESideType.Pmc);
                    Plugin.Log.LogInfo($"Miyako Carry Service brings its squad for the {(scav ? "scav" : "PMC")} raid");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Miyako Carry Service was not told the side of the raid: {ex.Message}");
                }
            }

            if (_pitSquad != null && squad != null && squad.Count > 0)
            {
                PitSquadPatch.Arm(map, squad, scav ? _pitScavSquad : _pitSquad);
            }
        }

        /// <summary>The resumed raid was not launched: what was handed over for it must not go to another raid.</summary>
        public static void AfterFailedLaunch()
        {
            PitSquadPatch.Disarm();
        }

        /// <summary>
        /// Must not be read from Awake: BepInEx fills its list as it loads the plugins, the ones loaded after us
        /// would be missed. The raids come long after.
        /// </summary>
        private static void Detect()
        {
            if (_detected)
            {
                return;
            }

            _detected = true;
            var pit = AssemblyOf(SquadPolicy.PitFireteamId);
            if (pit != null)
            {
                var holder = pit.GetType(PitSquadHolder);
                _pitSquad = ReadList(holder, "spawnMemberIds");
                _pitScavSquad = ReadList(holder, "spawnMemberIdsScav");
                Plugin.Log.LogInfo(
                    _pitSquad != null
                        ? "PIT Fireteam is installed: its squad is brought back by it when a raid is resumed"
                        : "PIT Fireteam is installed but its squad cannot be read: it will not come back when a raid is resumed"
                );
            }

            var miyako = AssemblyOf(SquadPolicy.MiyakoId);
            if (miyako != null)
            {
                _miyakoSide = miyako
                    .GetType(MiyakoSideHolder)
                    ?.GetField("CurrentType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (_miyakoSide?.FieldType != typeof(ESideType))
                {
                    _miyakoSide = null;
                }

                Plugin.Log.LogInfo(
                    _miyakoSide != null
                        ? "Miyako Carry Service is installed: its squad is brought back by it when a raid is resumed"
                        : "Miyako Carry Service is installed but its side cannot be set: its squad may come back for the wrong side"
                );
            }
        }

        private static Assembly AssemblyOf(string pluginId)
        {
            if (!SquadPolicy.IsAmong(Chainloader.PluginInfos.Keys, pluginId))
            {
                return null;
            }

            var info = Chainloader.PluginInfos.First(entry => string.Equals(entry.Key, pluginId, StringComparison.OrdinalIgnoreCase)).Value;
            return info?.Instance?.GetType().Assembly;
        }

        private static List<string> ReadList(Type holder, string name)
        {
            return holder?.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as List<string>;
        }
    }
}
