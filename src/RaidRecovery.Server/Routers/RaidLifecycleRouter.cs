using RaidRecovery.Server.Callbacks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace RaidRecovery.Server.Routers;

/// <summary>
/// Listens to raid start and end without replacing SPT: the server calls, one after the other, every router
/// that declares a URL. We run before SPT (Routers - 1) and return the output untouched; running after
/// would overwrite its response if it were streamed. The body is not deserialized (no declared type):
/// the end-of-raid one is large and of no use to us.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers - 1)]
public class RaidLifecycleRouter(JsonUtil jsonUtil, RaidRecoveryCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction(
                Routes.RaidStart,
                (url, info, sessionId, output, cancellationToken) => new ValueTask<object>(callbacks.RaidStarted(sessionId, output))
            ),
            new RouteAction(
                Routes.RaidEnd,
                (url, info, sessionId, output, cancellationToken) => new ValueTask<object>(callbacks.RaidEnded(sessionId, output))
            ),
        ]
    ) { }
