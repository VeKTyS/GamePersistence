using System.Linq;
using EFT;
using EFT.Interactive;
using RaidRecovery.Client.Models;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// Reads the state of the map: doors, extractions, searched containers. Runs on the main thread,
    /// so it only copies numbers and identifiers.
    /// </summary>
    internal sealed class WorldCapture
    {
        // Searching the scene is slow, and the doors of a map do not change during a raid: done once
        private WorldInteractiveObject[] _objects;

        public WorldDto Take(GameWorld gameWorld, Player player)
        {
            var world = new WorldDto { EntryPoint = player.Profile?.Info?.EntryPoint };

            if (_objects == null)
            {
                _objects = LocationScene.GetAllObjects<WorldInteractiveObject>().ToArray();
            }

            foreach (var item in _objects)
            {
                // A container is also an interactive object, but its lid is not worth restoring
                if (item == null || item is LootableContainer || string.IsNullOrEmpty(item.Id))
                {
                    continue;
                }

                var state = item.DoorState;
                // In the middle of its animation: no stable state to record, the object keeps the game's default
                if (state == EDoorState.None || (state & (EDoorState.Interacting | EDoorState.Breaching)) != 0)
                {
                    continue;
                }

                world.Objects[item.Id] = (byte)state;
            }

            var points = gameWorld.ExfiltrationController?.ExfiltrationPoints;
            if (points != null)
            {
                foreach (var point in points)
                {
                    var name = point?.Settings?.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        world.Exfils[name] = (byte)point.Status;
                    }
                }
            }

            if (player.SearchController is PlayerSearchController search)
            {
                foreach (var searched in search._searchedItems)
                {
                    if (searched != null)
                    {
                        world.Searched.Add(searched.Id.ToString());
                    }
                }
            }

            return world;
        }
    }
}
