using System.Reflection;
using RaidRecovery.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils;

namespace RaidRecovery.Server.Services;

/// <summary>
/// Applies a snapshot to the saved profile. We do not rewrite the inventory ourselves: we go through the two
/// functions SPT uses at the end of a raid, so that the result is that of a raid that would have ended here.
/// </summary>
[Injectable]
public class ProfileRestorer(
    ISptLogger<ProfileRestorer> logger,
    JsonUtil jsonUtil,
    ProfileHelper profileHelper,
    InRaidHelper inRaidHelper,
    HealthHelper healthHelper,
    SaveServer saveServer,
    TemplateTable templateTable,
    LocationLifecycleService raidLifecycle
)
{
    public const string PmcSide = "Pmc";

    public const string ScavSide = "Savage";

    /// <summary>A snapshot is restorable if it carries a profile and says which character it belongs to.</summary>
    public static bool IsRestorable(Snapshot snapshot)
    {
        return snapshot.Player?.Profile is not null && (IsSide(snapshot, PmcSide) || IsSide(snapshot, ScavSide));
    }

    private static bool IsSide(Snapshot snapshot, string side)
    {
        return string.Equals(snapshot.Raid?.Side, side, StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>null if the profile was updated and saved, otherwise the reason for the failure.</returns>
    public async Task<string?> ApplyAsync(MongoId sessionId, Snapshot snapshot, CancellationToken cancellationToken)
    {
        if (!IsRestorable(snapshot))
        {
            return "NotRestorable";
        }

        PmcData? captured;
        try
        {
            captured = jsonUtil.Deserialize<PmcData>(snapshot.Player!.Profile!.Value.GetRawText());
        }
        catch (Exception ex)
        {
            logger.Error("[RaidRecovery] Snapshot profile unreadable", ex);
            return "UnreadableProfile";
        }

        // Everything the two SPT functions need is checked before touching the profile:
        // we do not want a replaced inventory and a health left at its old value.
        if (captured?.Inventory?.Items is null || captured.Inventory.Equipment is null || captured.Inventory.QuestRaidItems is null)
        {
            return "IncompleteInventory";
        }

        if (captured.Health?.BodyParts is null)
        {
            return "IncompleteHealth";
        }

        var isScav = IsSide(snapshot, ScavSide);
        var profile = isScav ? profileHelper.GetScavProfile(sessionId) : profileHelper.GetPmcProfile(sessionId);
        if (profile is null)
        {
            return "ProfileNotFound";
        }

        if (isScav)
        {
            return await ApplyToScavAsync(sessionId, profile, captured, cancellationToken);
        }

        try
        {
            // isSurvived: the raid is not lost, items found in raid keep their mark
            inRaidHelper.SetInventory(sessionId, profile, captured, isSurvived: true, isTransfer: false);
            healthHelper.ApplyHealthChangesToProfile(profile, captured.Health, isDead: false);

            // Items examined during the raid. Absent from snapshots taken before version 0.2.2: left as is in that case.
            if (captured.Encyclopedia is not null)
            {
                profile.Encyclopedia = captured.Encyclopedia;
            }

            // Quest progress. Same handling as SPT at the end of a raid (HandlePostRaidPmc), without the automatic
            // delivery of the Lightkeeper rewards: that belongs to a raid end, not to a recovery.
            if (captured.Quests is not null)
            {
                profile.Quests = NormalizeQuests(captured.Quests);
            }

            if (captured.TaskConditionCounters is not null)
            {
                profile.TaskConditionCounters = captured.TaskConditionCounters;
            }

            ApplyProgress(sessionId, profile, captured);

            await saveServer.SaveProfileAsync(sessionId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error("[RaidRecovery] Applying the snapshot to the profile failed", ex);
            return "ApplyFailed";
        }

        return null;
    }

    /// <summary>
    /// A scav raid is resumed with the scav as it was in the raid. We take what SPT keeps of a scav at the end
    /// of a raid (HandlePostRaidPlayerScavAsync), minus what belongs to a raid that is over: the gear moved to
    /// the stash, the standing gained with Fence, the quest progress handed to the PMC. The true end of the
    /// resumed raid will do all that.
    /// </summary>
    private async Task<string?> ApplyToScavAsync(MongoId sessionId, PmcData scav, PmcData captured, CancellationToken cancellationToken)
    {
        try
        {
            // Written as it is, like SPT does for a scav: its health is not rebuilt from the changes of the raid
            scav.Health = captured.Health;
            inRaidHelper.SetInventory(sessionId, scav, captured, isSurvived: true, isTransfer: false);

            if (captured.Skills is not null)
            {
                scav.Skills = captured.Skills;
            }

            if (captured.Encyclopedia is not null)
            {
                scav.Encyclopedia = captured.Encyclopedia;
            }

            if (captured.TaskConditionCounters is not null)
            {
                scav.TaskConditionCounters = captured.TaskConditionCounters;
            }

            if (captured.Quests is not null)
            {
                scav.Quests = NormalizeQuests(captured.Quests);
            }

            await saveServer.SaveProfileAsync(sessionId, cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error("[RaidRecovery] Applying the snapshot to the scav failed", ex);
            return "ApplyFailed";
        }
    }

    /// <summary>
    /// The rest of what SPT keeps from a raid (HandlePostRaidPmc), in the same order. Each part is absent from
    /// snapshots taken before version 0.8.0, and is then left as it is in the profile.
    /// Left out on purpose: experience, level and statistics. The game works them out at the true end of the
    /// raid from the counters of the session, which the client carries into the resumed raid; applying them
    /// here as well would count the first half of the raid twice.
    /// </summary>
    private void ApplyProgress(MongoId sessionId, PmcData profile, PmcData captured)
    {
        if (captured.Skills is not null)
        {
            profile.Skills = captured.Skills;
        }

        if (captured.Achievements is not null)
        {
            // Rewards first: SPT finds the new achievements by comparing with those still in the profile.
            // Without its function we keep the old list, so the true end of the raid still hands the rewards out.
            var fullProfile = profileHelper.GetFullProfile(sessionId);
            if (fullProfile is not null && CallRaidEnd("ProcessAchievementRewards", fullProfile, captured.Achievements))
            {
                profile.Achievements = captured.Achievements;
            }
        }

        if (captured.WishList is not null)
        {
            profile.WishList = captured.WishList;
        }

        if (captured.Variables is not null)
        {
            profile.Variables = captured.Variables;
        }

        if (captured.TradersInfo is not null && profile.TradersInfo is not null)
        {
            CallRaidEnd("ApplyTraderStandingAdjustments", profile.TradersInfo, captured.TradersInfo);
        }

        if (captured.CheckedMagazines is not null)
        {
            profile.CheckedMagazines = captured.CheckedMagazines;
        }

        if (captured.CheckedChambers is not null)
        {
            profile.CheckedChambers = captured.CheckedChambers;
        }
    }

    /// <summary>
    /// Calls one of the functions SPT runs at the end of a raid. They are protected, so out of reach by a normal
    /// call; copying them here would drift at the first SPT update. Returns false if SPT no longer has it.
    /// </summary>
    private bool CallRaidEnd(string method, params object[] arguments)
    {
        try
        {
            var target = typeof(LocationLifecycleService).GetMethod(
                method,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                arguments.Select(argument => argument.GetType()).ToArray()
            );
            if (target is null)
            {
                logger.Warning($"[RaidRecovery] SPT no longer has {method}: this part of the raid is not restored");
                return false;
            }

            target.Invoke(raidLifecycle, arguments);
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[RaidRecovery] {method} failed: this part of the raid is not restored", ex);
            return false;
        }
    }

    /// <summary>
    /// The game returns a failed quest with the "MarkedAsFailed" status, which the saved profile does not know.
    /// Taken from LocationLifecycleService.ProcessPostRaidQuests, which is protected and so out of reach from here.
    /// </summary>
    private List<QuestStatus> NormalizeQuests(List<QuestStatus> quests)
    {
        foreach (var quest in quests.Where(q => q.Status == QuestStatusEnum.MarkedAsFailed))
        {
            if (templateTable.Quests.TryGetValue(quest.QId, out var template))
            {
                quest.Status = template.Restartable ? QuestStatusEnum.FailRestartable : QuestStatusEnum.Fail;
            }
        }

        return quests;
    }
}
