using RaidRecovery.Server.Callbacks;
using RaidRecovery.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace RaidRecovery.Server.Routers;

/// <summary>
/// The routes specific to the mod. Nobody else answers these URLs, so Routers + 1 as the 4.1 docs prescribe.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class RaidRecoveryRouter(JsonUtil jsonUtil, RaidRecoveryCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<Snapshot>(
                Routes.Save,
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Save(info, sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                Routes.Pending,
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Pending(sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                Routes.Restore,
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Restore(sessionId, cancellationToken)
            ),
            new RouteAction<EmptyRequestData>(
                Routes.Discard,
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Discard(sessionId)
            ),
            new RouteAction<ResumedRequest>(
                Routes.Resumed,
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Resumed(info)
            ),
        ]
    ) { }

public static class Routes
{
    public const string Save = "/raid-recovery/save";
    public const string Pending = "/raid-recovery/pending";
    public const string Restore = "/raid-recovery/restore";
    public const string Discard = "/raid-recovery/discard";
    public const string Resumed = "/raid-recovery/resumed";

    public const string RaidStart = "/client/match/local/start";
    public const string RaidEnd = "/client/match/local/end";
}
