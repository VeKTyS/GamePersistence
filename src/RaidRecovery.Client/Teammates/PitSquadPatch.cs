using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace RaidRecovery.Client.Teammates
{
    /// <summary>
    /// PIT Fireteam takes the squad of the raid from the group shown in the menu, when the raid settings are sent.
    /// A resumed raid skips that screen and the group is empty after a crash: the squad is put back in PIT's list
    /// right after PIT filled it, before it spawns the squad. Only enabled once PIT is found and a raid resumed.
    /// </summary>
    internal sealed class PitSquadPatch : ModulePatch
    {
        private static bool _enabled;
        private static string _map;
        private static List<string> _squad;
        private static List<string> _target;

        public static void Arm(string map, List<string> squad, List<string> target)
        {
            _map = map;
            _squad = squad;
            _target = target;
            if (_enabled)
            {
                return;
            }

            try
            {
                new PitSquadPatch().Enable();
                _enabled = true;
            }
            catch (Exception ex)
            {
                Disarm();
                Plugin.Log.LogWarning($"PIT Fireteam will not get its squad back: {ex.Message}");
            }
        }

        public static void Disarm()
        {
            _map = null;
            _squad = null;
            _target = null;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EftClientBackendSession), nameof(EftClientBackendSession.SendRaidSettings));
        }

        /// <summary>Last: PIT's own postfix empties its list first.</summary>
        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void PatchPostfix(RaidSettings settings)
        {
            var map = _map;
            var squad = _squad;
            var target = _target;
            // Used once, whatever the raid: a raid started by hand later must not get this squad
            Disarm();
            if (squad == null || target == null)
            {
                return;
            }

            try
            {
                if (!SquadPolicy.IsRaidOf(map, settings?.LocationId))
                {
                    Plugin.Log.LogWarning($"Raid launched on {settings?.LocationId} instead of {map}: PIT Fireteam's squad is not brought back");
                    return;
                }

                var added = SquadPolicy.AddMissing(target, squad);
                Plugin.Log.LogInfo($"PIT Fireteam brings back {added} teammates of the interrupted raid");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"PIT Fireteam's squad was not brought back: {ex}");
            }
        }
    }
}
