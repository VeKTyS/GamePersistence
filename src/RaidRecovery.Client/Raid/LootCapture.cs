using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using JsonType;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// What the plugin remembers of the loot from one snapshot to the next, for the length of a raid.
    /// </summary>
    internal sealed class LootMemory
    {
        /// <summary>Every loot item seen on the map at least once since the raid started.</summary>
        public readonly HashSet<string> EverSeen = new HashSet<string>();

        /// <summary>Where each loose item lay the first time it was seen, once it had come to rest.</summary>
        public readonly Dictionary<string, Vector3> FirstSeenAt = new Dictionary<string, Vector3>();

        /// <summary>Items the map itself put there. Anything else lying around was dropped, by the player or by a bot.</summary>
        public HashSet<string> PutByMap;
    }

    /// <summary>
    /// What became of the loot of the map. Two things are read: the items that were there and no longer are,
    /// and the full description of those lying where the map did not put them. The server removes the first
    /// from the loot of the raid, and adds the second.
    /// We compare what the game sees with what it saw earlier, never with what the server generated: some of
    /// the loot the server sends is never put on the map (quest items among others), and judging on the
    /// server's list would remove them for good.
    /// </summary>
    internal sealed class LootCapture
    {
        // Containers, then loose items, read per batch so that no frame pays for the whole map
        private const int ContainersPerStep = 20;
        private const int ItemsPerStep = 40;

        // An item counts as moved beyond this distance from where it first lay. Under it, it only rolled.
        private const float MovedDistance = 0.75f;

        private readonly LootMemory _memory;
        private readonly HashSet<string> _present = new HashSet<string>();
        private readonly List<string> _gone = new List<string>();
        private readonly List<LootItemSerializer> _loose = new List<LootItemSerializer>();

        public LootCapture(LootMemory memory)
        {
            _memory = memory;
        }

        public int PresentCount => _present.Count;

        public int GoneCount => _gone.Count;

        public int LooseCount => _loose.Count;

        /// <summary>Lists what there is to read and adds the steps that read it to the pass.</summary>
        public void Plan(CapturePass pass, GameWorld gameWorld)
        {
            if (_memory.PutByMap == null)
            {
                _memory.PutByMap = new HashSet<string>(
                    gameWorld.AllLoot.Where(entry => entry?.Item != null).Select(entry => (string)entry.Item.Id)
                );
            }

            var containers = gameWorld.LootList.OfType<LootableContainer>().ToList();
            for (var start = 0; start < containers.Count; start += ContainersPerStep)
            {
                var batch = containers.Skip(start).Take(ContainersPerStep).ToList();
                pass.Add("containers", () => ReadContainers(batch), required: false);
            }

            // Bodies are loot items too, but they travel apart, with their bones and their look
            var items = gameWorld.LootList.OfType<LootItem>().Where(item => !(item is Corpse)).ToList();
            for (var start = 0; start < items.Count; start += ItemsPerStep)
            {
                var batch = items.Skip(start).Take(ItemsPerStep).ToList();
                pass.Add("loose loot", () => ReadItems(batch), required: false);
            }

            pass.Add("loot changes", Compare, required: false);
        }

        private void ReadContainers(List<LootableContainer> containers)
        {
            foreach (var container in containers)
            {
                var root = container == null ? null : container.ItemOwner?.RootItem;
                if (root == null)
                {
                    continue;
                }

                foreach (var item in root.GetAllItems())
                {
                    _present.Add(item.Id);
                }
            }
        }

        private void ReadItems(List<LootItem> items)
        {
            foreach (var lootItem in items)
            {
                // Picked up or destroyed since the list was made
                var root = lootItem == null ? null : lootItem.Item;
                if (root == null)
                {
                    continue;
                }

                foreach (var item in root.GetAllItems())
                {
                    _present.Add(item.Id);
                }

                string id = root.Id;
                var position = lootItem.transform.position;
                if (!_memory.FirstSeenAt.TryGetValue(id, out var first))
                {
                    _memory.FirstSeenAt[id] = position;
                    first = position;
                }

                var dropped = !_memory.PutByMap.Contains(id);
                var moved = Vector3.Distance(first, position) > MovedDistance;
                if (dropped || moved)
                {
                    _loose.Add(Describe(lootItem, root, position));
                }
            }
        }

        /// <summary>Runs once everything is read: what was seen before and is not seen now has left the map.</summary>
        private void Compare()
        {
            foreach (var id in _memory.EverSeen)
            {
                if (!_present.Contains(id))
                {
                    _gone.Add(id);
                }
            }

            _memory.EverSeen.UnionWith(_present);
        }

        private static LootItemSerializer Describe(LootItem lootItem, Item root, Vector3 position)
        {
            var entry = new JsonLootItem
            {
                Id = "RaidRecovery_" + root.Id,
                Position = position,
                Rotation = lootItem.transform.rotation.eulerAngles,
                Item = root,
                IsContainer = false,
                // It already lies where it fell: letting it fall again would move it
                useGravity = false,
                randomRotation = false,
            };

            // The game's own writer for the loot of a raid: the format it reads back at the next raid start
            var serializer = new LootItemSerializer();
            ((ISerializer<JsonLootItem>)serializer).Serialize(entry);
            return serializer;
        }

        public List<string> GoneIds()
        {
            return _gone.ToList();
        }

        public List<JRaw> LooseToJson()
        {
            return _loose.Select(entry => new JRaw(entry.ToJson())).ToList();
        }
    }
}
