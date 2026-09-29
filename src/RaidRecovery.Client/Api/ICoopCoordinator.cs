using System.Threading.Tasks;
using EFT;

namespace RaidRecovery.Client.Api
{
    /// <summary>
    /// What a mod that plays raids with others tells Raid Recovery. Raid Recovery keeps saving and restoring
    /// by itself: the coordinator only says when, and prepares what its own mod needs.
    /// </summary>
    public interface ICoopCoordinator
    {
        /// <summary>Written in the log, to tell who decided.</summary>
        string Name { get; }

        /// <summary>
        /// Whether this player saves the raid that starts. Asked at each raid start: the same player hosts
        /// one raid and joins the next.
        /// </summary>
        bool SavesThisRaid { get; }

        /// <summary>
        /// Called on the main thread, right before the raid of a recovery is launched. The map, the side and
        /// the time slot are already set.
        /// </summary>
        Task BeforeLaunchAsync(RaidSettings settings);
    }
}
