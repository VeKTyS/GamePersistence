using System.Reflection;
using RaidRecovery.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Weather;

namespace RaidRecovery.Server.Patches;

/// <summary>Runs right after SPT generated the weather of a raid, before the response leaves for the game.</summary>
public class WeatherGeneratedPatch : AbstractPatch
{
    // A patch is a static method: SPT cannot inject anything into it, the host hands these over at load time
    internal static WeatherReplayService? Service { get; set; }

    internal static ISptLogger<RaidRecoveryHost>? Logger { get; set; }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(WeatherController).GetMethod(nameof(WeatherController.GenerateLocal))
            ?? throw new MissingMethodException(nameof(WeatherController), nameof(WeatherController.GenerateLocal));
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, ref GetLocalWeatherResponseData __result)
    {
        // Must never throw: an exception here would leave the raid without weather
        try
        {
            if (Service is null || __result is null)
            {
                return;
            }

            var kept = Service.OnGenerated(sessionId.ToString(), __result);
            if (kept is null)
            {
                return;
            }

            __result = kept;
            Logger?.Success($"[RaidRecovery] Weather of the interrupted raid served again: {kept.Weather?.Count ?? 0} forecasts");
        }
        catch (Exception ex)
        {
            Logger?.Error("[RaidRecovery] Weather replay failed, the raid gets the weather of the server", ex);
        }
    }
}
