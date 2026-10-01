using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using EFT;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>
    /// The game only draws what it loaded first: heads, clothes, weapons. It loads them when it generates a bot
    /// or reads the loot of a raid. What we put back ourselves skips those two paths, so we load it ourselves.
    /// </summary>
    internal static class GameAssets
    {
        /// <summary>
        /// Same call as the game when it prepares a bot (BotProfileClient.CreateProfile). Immediate during the loading: the
        /// game has nothing more urgent to do then. General otherwise.
        /// </summary>
        public static Task LoadAsync(IEnumerable<ResourceKey> resources, YieldDelegate priority = null)
        {
            var wanted = resources.Where(resource => resource != null && !string.IsNullOrEmpty(resource.path)).ToArray();
            if (wanted.Length == 0)
            {
                return Task.CompletedTask;
            }

            return Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                ObjectsFactory.PoolsCategory.Raid,
                // A raid of SPT is a local one. Asking for the online set fails: it does not exist here.
                ObjectsFactory.AssemblyType.Local,
                wanted,
                priority ?? JobYieldPriority.General,
                null,
                ObjectsFactory.DefaultCancellationToken
            );
        }
    }
}
