#nullable disable
using System;

namespace RaidRecovery.Fika.Client
{
    /// <summary>Where the raid of the host stands, seen from a player who wants to go back to it.</summary>
    public enum HostRaid
    {
        /// <summary>No raid of the host is listed.</summary>
        None,

        /// <summary>The host is loading: nobody can join yet.</summary>
        Loading,

        /// <summary>The host waits for its players, the raid has not started.</summary>
        Waiting,

        /// <summary>The raid is running and this player is one of its players, alive.</summary>
        RunningWithPlayer,

        /// <summary>The raid is running and this player died in it.</summary>
        RunningPlayerDead,

        /// <summary>The raid is running and this player is not one of its players.</summary>
        RunningWithoutPlayer,
    }

    public enum Rejoin
    {
        /// <summary>The host is not back, or not ready: the player waits and tries again.</summary>
        Wait,

        /// <summary>The host waits in its resumed raid: the character is restored, then the raid is joined.</summary>
        JoinRestored,

        /// <summary>The player is still known to a running raid: the reconnection of Fika brings them back.</summary>
        Reconnect,

        /// <summary>The raid went on without the player, or ended: there is nothing to go back to.</summary>
        Gone,
    }

    /// <summary>
    /// What a player who joined a raid may do once it was cut. Knows nothing of the game nor of Fika, so it
    /// can be tested without them.
    /// </summary>
    internal static class RejoinPolicy
    {
        public const string HostMark = "host";
        private const string ClientPrefix = "client:";

        public static string MarkOfClient(string hostProfileId)
        {
            return ClientPrefix + hostProfileId;
        }

        /// <summary>The host of the raid a snapshot was taken in, or null when the player hosted it, or played alone.</summary>
        public static string HostOf(string mark)
        {
            if (string.IsNullOrEmpty(mark) || !mark.StartsWith(ClientPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            var host = mark.Substring(ClientPrefix.Length).Trim();
            return host.Length == 0 ? null : host;
        }

        /// <summary>The numbers are the ones of Fika's lobby status: 0 loading, 1 in game, 2 waiting for players.</summary>
        public static HostRaid Read(bool listed, int status, bool playerInRaid, bool playerDead)
        {
            if (!listed)
            {
                return HostRaid.None;
            }

            switch (status)
            {
                case 0:
                    return HostRaid.Loading;
                case 2:
                    return HostRaid.Waiting;
                case 1:
                    if (!playerInRaid)
                    {
                        return HostRaid.RunningWithoutPlayer;
                    }

                    return playerDead ? HostRaid.RunningPlayerDead : HostRaid.RunningWithPlayer;
                default:
                    return HostRaid.None;
            }
        }

        /// <summary>
        /// The host resumed since the snapshot when the server dated its resume after it. A raid listed
        /// before that is the one that was cut: the server of Fika keeps it a few minutes after its host is gone.
        /// </summary>
        public static bool HostResumedSince(DateTimeOffset? hostResumedAt, DateTimeOffset? snapshotSavedAt)
        {
            return hostResumedAt.HasValue && (!snapshotSavedAt.HasValue || hostResumedAt.Value >= snapshotSavedAt.Value);
        }

        public static Rejoin Decide(bool hostResumed, HostRaid raid)
        {
            // Known to a running raid: it is the player who was cut, not the host. Also true in a resumed raid
            // the player had joined before being cut again.
            if (raid == HostRaid.RunningWithPlayer)
            {
                return Rejoin.Reconnect;
            }

            if (!hostResumed)
            {
                return Rejoin.Wait;
            }

            switch (raid)
            {
                case HostRaid.Waiting:
                    return Rejoin.JoinRestored;
                case HostRaid.Loading:
                    return Rejoin.Wait;
                default:
                    return Rejoin.Gone;
            }
        }

        /// <summary>
        /// Leaving keeps the gear when the raid did go on: the host resumed it, or it never stopped. When the
        /// host never came back the raid is void for everyone, and leaving gives back the character of before it.
        /// </summary>
        public static bool LeaveKeepsGear(bool hostResumed, HostRaid raid)
        {
            return hostResumed || raid == HostRaid.RunningWithPlayer || raid == HostRaid.RunningPlayerDead;
        }

        public static string Explain(Rejoin rejoin, HostRaid raid)
        {
            switch (rejoin)
            {
                case Rejoin.Wait:
                    return raid == HostRaid.Loading
                        ? "The host is loading the raid. Try again in a moment."
                        : "The host has not reconnected yet. Try again once the host is back in the raid lobby.";
                case Rejoin.Gone:
                    return raid == HostRaid.RunningPlayerDead
                        ? "You died in this raid. Leave it to get back to the menu."
                        : "The host resumed the raid without you, or the raid is over. Leave it: you keep the gear you carried.";
                default:
                    return null;
            }
        }
    }
}
