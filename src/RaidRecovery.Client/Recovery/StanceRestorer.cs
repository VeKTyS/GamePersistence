using System;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using RaidRecovery.Client.Models;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// How the player stood when the raid was cut: crouched or lying down, out of breath or not, and what they
    /// held. The game starts every raid standing, rested, with the first weapon it finds. Each part is
    /// independent: a weapon that cannot be taken in hands must not leave the player standing in the open.
    /// </summary>
    internal static class StanceRestorer
    {
        public static void Apply(Player player, StanceDto stance)
        {
            if (stance == null)
            {
                return;
            }

            Run("posture", () => RestorePosture(player, stance));
            Run("stamina", () => RestoreStamina(player, stance));
            Run("item in hands", () => RestoreHands(player, stance));
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

        private static void RestorePosture(Player player, StanceDto stance)
        {
            var movement = player.MovementContext;
            if (movement == null)
            {
                return;
            }

            // The game's own toggle: it checks there is room to lie down, and plays the transition
            if (stance.Prone && !movement.IsInPronePose && movement.CanProne)
            {
                player.ToggleProne();
            }
            else if (!stance.Prone)
            {
                movement.SetPoseLevel(stance.PoseLevel, true);
            }

            Plugin.Log.LogInfo($"Posture restored: {(stance.Prone ? "lying down" : $"height {stance.PoseLevel:0.00}")}");
        }

        private static void RestoreStamina(Player player, StanceDto stance)
        {
            var physical = player.Physical;
            if (physical == null)
            {
                return;
            }

            // Never above what the character can hold now: its capacity follows its skills and its load
            Set(physical.Stamina, stance.Stamina);
            Set(physical.HandsStamina, stance.HandsStamina);
            Set(physical.Oxygen, stance.Oxygen);
            Plugin.Log.LogInfo($"Stamina restored: legs {physical.Stamina?.Current:0}, arms {physical.HandsStamina?.Current:0}");
        }

        private static void Set(Stamina stamina, float? value)
        {
            if (stamina == null || value == null)
            {
                return;
            }

            stamina.Current = Math.Max(0f, Math.Min(value.Value, stamina.TotalCapacity.Value));
        }

        private static void RestoreHands(Player player, StanceDto stance)
        {
            if (string.IsNullOrEmpty(stance.InHands))
            {
                return;
            }

            if (player.HandsController?.Item != null && player.HandsController.Item.Id == stance.InHands)
            {
                return;
            }

            Item item = player.Profile.Inventory.Equipment.GetAllItems().FirstOrDefault(candidate => candidate.Id == stance.InHands);
            if (item == null)
            {
                Plugin.Log.LogWarning("The item the player held is no longer in their gear: the game keeps the one it chose");
                return;
            }

            player.TryProceed(item, null, true);
            Plugin.Log.LogInfo($"Item in hands restored: {item.ShortName}");
        }
    }
}
