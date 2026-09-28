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
    TemplateTable templateTable
)
{
    public const string PmcSide = "Pmc";

    /// <summary>A snapshot is restorable if it carries a profile and comes from a PMC raid.</summary>
    public static bool IsRestorable(Snapshot snapshot)
    {
        return snapshot.Player?.Profile is not null && string.Equals(snapshot.Raid?.Side, PmcSide, StringComparison.OrdinalIgnoreCase);
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

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null)
        {
            return "ProfileNotFound";
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
