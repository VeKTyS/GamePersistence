using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using RaidRecovery.Client.Models;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// Puts the map back in the state of the snapshot. Each part is independent: a door that fails
    /// must not prevent the extractions from being restored.
    /// </summary>
    internal static class WorldRestorer
    {
        /// <summary>
        /// To call before the game initializes the extractions: it reads the entry point at that moment
        /// to decide which ones belong to the player.
        /// </summary>
        public static void RestoreEntryPoint(WorldDto world)
        {
            if (string.IsNullOrEmpty(world?.EntryPoint))
            {
                return;
            }

            var game = Singleton<AbstractGame>.Instance;
            var field = game == null ? null : AccessTools.Field(game.GetType(), "_entryPoint");
            if (field == null)
            {
                Plugin.Log.LogWarning("Entry point not restored: the game no longer exposes it");
                return;
            }

            var chosen = field.GetValue(game) as string;
            field.SetValue(game, world.EntryPoint);
            Plugin.Log.LogInfo($"Entry point restored: {world.EntryPoint} (the game had chosen {chosen ?? "none"})");
        }

        public static void Apply(GameWorld gameWorld, Player player, WorldDto world)
        {
            if (world == null)
            {
                return;
            }

            Run("entry point", () => CheckEntryPoint(player, world));
            Run("doors", () => RestoreObjects(world));
            Run("extractions", () => RestoreExfils(gameWorld, world));
            Run("searched containers", () => RestoreSearched(player, world));
        }

        private static void Run(string part, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not restore the {part}: {ex}");
            }
        }

        private static void CheckEntryPoint(Player player, WorldDto world)
        {
            var current = player.Profile?.Info?.EntryPoint;
            if (!string.IsNullOrEmpty(world.EntryPoint) && !string.Equals(current, world.EntryPoint, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogWarning($"Entry point is {current}, expected {world.EntryPoint}: the extractions may differ from the interrupted raid");
            }
        }

        private static void RestoreObjects(WorldDto world)
        {
            if (world.Objects == null || world.Objects.Count == 0)
            {
                return;
            }

            var changed = 0;
            foreach (var item in LocationScene.GetAllObjects<WorldInteractiveObject>())
            {
                if (item == null || item is LootableContainer || string.IsNullOrEmpty(item.Id))
                {
                    continue;
                }

                if (!world.Objects.TryGetValue(item.Id, out var saved) || (byte)item.DoorState == saved)
                {
                    continue;
                }

                var state = (EDoorState)saved;
                if (item is Switch lever && state == EDoorState.Open)
                {
                    // A switch does more than move: it powers an extraction, opens a gate. Only its own
                    // Open() runs that chain.
                    lever.Open();
                }
                else
                {
                    // The path the game takes when it joins a raid already in progress: state and angle, no animation
                    item.SetInitialSyncState(new WorldInteractiveObject.InteractiveObjectStatusInfo(item.Id, state, 0f));
                }

                changed++;
            }

            Plugin.Log.LogInfo($"Doors and switches restored: {changed} changed out of {world.Objects.Count} recorded");
        }

        private static void RestoreExfils(GameWorld gameWorld, WorldDto world)
        {
            var points = gameWorld.ExfiltrationController?.ExfiltrationPoints;
            if (points == null || world.Exfils == null || world.Exfils.Count == 0)
            {
                return;
            }

            var changed = 0;
            foreach (var point in points)
            {
                var name = point?.Settings?.Name;
                if (string.IsNullOrEmpty(name) || !world.Exfils.TryGetValue(name, out var saved))
                {
                    continue;
                }

                var status = (EExfiltrationStatus)saved;
                if (point.Status == status || !IsStable(status))
                {
                    continue;
                }

                point.SetStatusLogged(status, "RaidRecovery");
                changed++;
            }

            Plugin.Log.LogInfo($"Extractions restored: {changed} changed out of {world.Exfils.Count} recorded");
        }

        /// <summary>
        /// A countdown or a wait for activation belongs to a player standing in the extraction at that moment.
        /// The resumed player is not in it: those states are left to the game.
        /// </summary>
        private static bool IsStable(EExfiltrationStatus status)
        {
            return status == EExfiltrationStatus.NotPresent
                || status == EExfiltrationStatus.RegularMode
                || status == EExfiltrationStatus.UncompleteRequirements
                || status == EExfiltrationStatus.Hidden;
        }

        private static void RestoreSearched(Player player, WorldDto world)
        {
            if (world.Searched == null || world.Searched.Count == 0)
            {
                return;
            }

            if (!(player.SearchController is ActiveSearchController search))
            {
                Plugin.Log.LogWarning($"Searched containers not restored: unexpected search controller {player.SearchController?.GetType().Name}");
                return;
            }

            var searched = new HashSet<string>(world.Searched, StringComparer.OrdinalIgnoreCase);
            var restored = 0;
            foreach (var container in LocationScene.GetAllObjects<LootableContainer>())
            {
                var root = container?.ItemOwner?.RootItem;
                if (root == null || !searched.Contains(root.Id.ToString()))
                {
                    continue;
                }

                // Marks the container as searched and everything inside as seen
                search.UncoverContent(root);
                restored++;
            }

            Plugin.Log.LogInfo($"Searched containers restored: {restored} found out of {searched.Count} recorded");
        }
    }
}
