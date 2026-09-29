using System;
using System.Threading.Tasks;
using EFT;

namespace RaidRecovery.Client.Api
{
    /// <summary>What a player saves of the raid they are in.</summary>
    public enum CaptureScope
    {
        /// <summary>Nothing: the raid is not saved from this machine.</summary>
        Nothing,

        /// <summary>The character only, and only when asked: the world belongs to another player.</summary>
        Character,

        /// <summary>The character, the map, the loot and the bots, as in a raid played alone.</summary>
        Everything,
    }

    /// <summary>The raid a snapshot was taken in, as the player is offered to go back to it.</summary>
    public sealed class InterruptedRaid
    {
        public string Map { get; set; }

        /// <summary>Pmc or Savage.</summary>
        public string Side { get; set; }

        /// <summary>What <see cref="ICoopCoordinator.DescribeThisRaid"/> answered when the snapshot was taken. null: played alone.</summary>
        public string Coop { get; set; }

        /// <summary>When the server received the snapshot, by its own clock.</summary>
        public DateTimeOffset? SavedAt { get; set; }
    }

    public enum ResumeWay
    {
        /// <summary>The raid cannot be gone back to now. The player is told why and keeps the choice.</summary>
        Refused,

        /// <summary>Raid Recovery restores the character from its snapshot, then the raid is launched.</summary>
        Restored,

        /// <summary>The co-op mod brings the player back by itself: the raid is launched, nothing is restored.</summary>
        Handled,
    }

    public sealed class ResumeAnswer
    {
        public ResumeWay Way { get; set; }

        /// <summary>Shown to the player when the raid is refused.</summary>
        public string Reason { get; set; }

        public static ResumeAnswer Refuse(string reason) => new ResumeAnswer { Way = ResumeWay.Refused, Reason = reason };

        public static readonly ResumeAnswer Restore = new ResumeAnswer { Way = ResumeWay.Restored };

        public static readonly ResumeAnswer Handle = new ResumeAnswer { Way = ResumeWay.Handled };
    }

    /// <summary>
    /// What a mod that plays raids with others tells Raid Recovery. Raid Recovery keeps saving and restoring
    /// by itself: the coordinator only says when, and prepares what its own mod needs.
    /// Everything here is called on the main thread.
    /// </summary>
    public interface ICoopCoordinator
    {
        /// <summary>Written in the log, to tell who decided.</summary>
        string Name { get; }

        /// <summary>
        /// What this player saves of the raid that starts. Asked at each raid start: the same player hosts
        /// one raid and joins the next.
        /// </summary>
        CaptureScope ScopeOfThisRaid { get; }

        /// <summary>
        /// Who this player is in the raid in progress. Kept in the snapshot and given back in
        /// <see cref="InterruptedRaid.Coop"/>. Raid Recovery does not read it.
        /// </summary>
        string DescribeThisRaid();

        /// <summary>Asked when the player chooses to go back to the raid, before anything is restored.</summary>
        Task<ResumeAnswer> BeforeResumeAsync(InterruptedRaid raid);

        /// <summary>
        /// Called right before the raid is launched. The map, the side and the time slot are already set.
        /// Throws when the raid cannot be launched after all.
        /// </summary>
        Task BeforeLaunchAsync(InterruptedRaid raid, RaidSettings settings, ResumeWay way);

        /// <summary>
        /// Asked when the player chooses to leave the raid for good. true: they keep what they carried at the
        /// last snapshot. false: they get back the character of before the raid.
        /// </summary>
        Task<bool> LeaveKeepsGearAsync(InterruptedRaid raid);
    }
}
