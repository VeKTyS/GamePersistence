using System;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using Fika.Core.Main.Utils;
using Fika.Core.Networking.Http;
using Fika.Core.UI.Custom;
using Fika.Core.UI.Models;
using HarmonyLib;
using RaidRecovery.Client.Api;
using SPT.Reflection.Utils;

namespace RaidRecovery.Fika.Client
{
    /// <summary>What Fika needs Raid Recovery to know: who saves what, and how a raid is hosted or joined.</summary>
    internal sealed class FikaCoordinator : ICoopCoordinator
    {
        public string Name => "Raid Recovery Fika";

        /// <summary>
        /// The host runs the raid: bots, loot and doors only exist for good on its machine. The others save
        /// their character. A host without a screen is left out, nobody sits in front of it to resume anything.
        /// </summary>
        public CaptureScope ScopeOfThisRaid
        {
            get
            {
                if (FikaBackendUtils.IsHeadless || FikaBackendUtils.IsSpectator)
                {
                    return CaptureScope.Nothing;
                }

                return FikaBackendUtils.IsServer ? CaptureScope.Everything : CaptureScope.Character;
            }
        }

        public string DescribeThisRaid()
        {
            // For a player who joined, the identifier of the group is the profile of its host
            return FikaBackendUtils.IsServer ? RejoinPolicy.HostMark : RejoinPolicy.MarkOfClient(FikaBackendUtils.GroupId);
        }

        public async Task<ResumeAnswer> BeforeResumeAsync(InterruptedRaid raid)
        {
            var host = RejoinPolicy.HostOf(raid.Coop);
            if (host == null)
            {
                return ResumeAnswer.Restore;
            }

            var (resumed, hostRaid) = await LookForAsync(host, raid);
            var rejoin = RejoinPolicy.Decide(resumed, hostRaid);
            Plugin.Log.LogInfo($"Host {host}: resumed since the snapshot {resumed}, its raid {hostRaid}, so {rejoin}");

            switch (rejoin)
            {
                case Rejoin.JoinRestored:
                    return ResumeAnswer.Restore;
                case Rejoin.Reconnect:
                    return ResumeAnswer.Handle;
                default:
                    return ResumeAnswer.Refuse(RejoinPolicy.Explain(rejoin, hostRaid));
            }
        }

        public async Task<bool> LeaveKeepsGearAsync(InterruptedRaid raid)
        {
            var host = RejoinPolicy.HostOf(raid.Coop);
            if (host == null)
            {
                // The host leaves its own raid: as alone, the character of before the raid
                return false;
            }

            var (resumed, hostRaid) = await LookForAsync(host, raid);
            var keeps = RejoinPolicy.LeaveKeepsGear(resumed, hostRaid);
            Plugin.Log.LogInfo($"Host {host}: resumed since the snapshot {resumed}, its raid {hostRaid}, so leaving keeps the gear: {keeps}");
            return keeps;
        }

        public async Task BeforeLaunchAsync(InterruptedRaid raid, RaidSettings settings, ResumeWay way)
        {
            // Fika sets this on its own screen, which the recovery does not go through. Its setter is not
            // public: this is one of the two places where the add-on reaches inside Fika.
            AccessTools.PropertySetter(typeof(FikaBackendUtils), nameof(FikaBackendUtils.IsScav))?.Invoke(null, new object[] { settings.IsScav });

            var profile = FikaBackendUtils.Profile;
            var host = RejoinPolicy.HostOf(raid.Coop);
            if (host == null)
            {
                // What the "host" button of Fika does before it lets the game launch the raid
                await FikaBackendUtils.CreateMatch(profile.ProfileId, FikaBackendUtils.PMCName, settings);
                Plugin.Log.LogInfo($"Raid declared to Fika on {settings.LocationId}, code {FikaBackendUtils.RaidCode}");
                return;
            }

            // What the "join" button of Fika does. It shows its own error screen when the host cannot be reached
            var reconnect = way == ResumeWay.Handled;
            var joined = await MatchMakerUIScript.JoinMatch(profile.ProfileId, host, null, reconnect);
            if (!joined)
            {
                throw new InvalidOperationException("the host could not be reached");
            }

            Plugin.Log.LogInfo($"Raid of {host} joined on {settings.LocationId}, reconnection of Fika: {reconnect}");
        }

        /// <summary>Asks the two servers: ours for the resume of the host, the one of Fika for its raid.</summary>
        private static async Task<(bool Resumed, HostRaid Raid)> LookForAsync(string host, InterruptedRaid raid)
        {
            var profileId = FikaBackendUtils.Profile.ProfileId;
            var settings = ClientAppUtils.GetMainApp()?._raidSettings;

            // Both calls wait for the network: they leave the main thread, the answer comes back to it
            var (resumedAt, raids) = await Task.Run(async () =>
            {
                var at = await RaidRecoveryApi.LastResumeOfAsync(host).ConfigureAwait(false);
                return (at, FikaRequestHandler.LocationRaids(settings));
            });

            var resumed = RejoinPolicy.HostResumedSince(resumedAt, raid.SavedAt);
            var entries = (raids ?? Array.Empty<LobbyEntry>()).Where(entry => entry.ServerId == host).ToArray();
            if (entries.Length == 0)
            {
                return (resumed, HostRaid.None);
            }

            var entry = entries[0];
            var dead = false;
            var inRaid = entry.Players != null && entry.Players.TryGetValue(profileId, out dead);
            return (resumed, RejoinPolicy.Read(true, (int)entry.Status, inRaid, dead));
        }
    }
}
