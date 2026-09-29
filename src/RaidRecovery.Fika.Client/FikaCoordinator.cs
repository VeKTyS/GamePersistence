using System.Threading.Tasks;
using EFT;
using Fika.Core.Main.Utils;
using HarmonyLib;
using RaidRecovery.Client.Api;

namespace RaidRecovery.Fika.Client
{
    /// <summary>What Fika needs Raid Recovery to know: who saves the raid, and how a raid is hosted.</summary>
    internal sealed class FikaCoordinator : ICoopCoordinator
    {
        public string Name => "Raid Recovery Fika";

        /// <summary>
        /// The host runs the raid: bots, loot and doors only exist for good on its machine. A host without a
        /// screen is left out, nobody sits in front of it to resume anything.
        /// </summary>
        public bool SavesThisRaid => FikaBackendUtils.IsServer && !FikaBackendUtils.IsHeadless;

        /// <summary>
        /// Does what the "host" button of Fika does before it lets the game launch the raid: the raid is
        /// declared to the server, and this player becomes its host.
        /// </summary>
        public async Task BeforeLaunchAsync(RaidSettings settings)
        {
            // Fika sets this on its own screen, which the recovery does not go through. Its setter is not
            // public: this is the one place where the add-on reaches inside Fika.
            AccessTools.PropertySetter(typeof(FikaBackendUtils), nameof(FikaBackendUtils.IsScav))?.Invoke(null, new object[] { settings.IsScav });

            var profile = FikaBackendUtils.Profile;
            await FikaBackendUtils.CreateMatch(profile.ProfileId, FikaBackendUtils.PMCName, settings);
            Plugin.Log.LogInfo($"Raid declared to Fika on {settings.LocationId}, code {FikaBackendUtils.RaidCode}");
        }
    }
}
