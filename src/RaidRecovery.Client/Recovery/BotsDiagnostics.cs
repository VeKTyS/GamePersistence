using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// Writes to the log what each bot of a resumed raid knows about the player. Only there to tell apart
    /// a bot that does not see the player from one that does not consider them an enemy.
    /// </summary>
    internal static class BotsDiagnostics
    {
        public static void Log(GameWorld gameWorld, ICollection<string> restoredIds)
        {
            var player = gameWorld?.MainPlayer;
            if (player == null)
            {
                return;
            }

            foreach (var other in gameWorld.AllAlivePlayersList)
            {
                try
                {
                    var owner = other == null || !other.IsAI ? null : other.AIData?.BotOwner;
                    if (owner == null)
                    {
                        continue;
                    }

                    var group = owner.BotsGroup;
                    var origin = restoredIds.Contains(other.ProfileId) ? "restored" : "spawned by the game";
                    Plugin.Log.LogInfo(
                        $"Bot {other.Profile.Nickname} ({origin}, {other.Profile.Info.Settings.Role}, side {other.Profile.Side}): "
                            + $"state {owner.BotState}, standby {owner.StandBy?.StandByType}, "
                            + $"distance {Vector3.Distance(other.Position, player.Position):0} m, "
                            + $"player is enemy of its group: {group?.Enemies?.ContainsKey(player)}, "
                            + $"player is neutral: {group?.Neutrals?.ContainsKey(player)}, "
                            + $"group enemies {group?.Enemies?.Count}, has a target: {owner.Memory?.GoalEnemy != null}"
                    );
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Bot not described: {ex.Message}");
                }
            }
        }
    }
}
