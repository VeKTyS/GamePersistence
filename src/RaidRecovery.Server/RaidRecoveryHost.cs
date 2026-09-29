using System.Reflection;
using RaidRecovery.Server.Patches;
using RaidRecovery.Server.Services;
using RaidRecovery.Server.Storage;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Weather;
using SPTarkov.Server.Core.Utils;

namespace RaidRecovery.Server;

/// <summary>
/// Entry point on the SPT side: builds the service once and announces the mod in the log.
/// Singleton, because the service remembers which raids are open or closed.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad + 1)]
public class RaidRecoveryHost : IOnLoad
{
    private readonly ISptLogger<RaidRecoveryHost> _logger;
    private readonly string? _configWarning;

    public RaidRecoveryHost(ISptLogger<RaidRecoveryHost> logger, JsonUtil jsonUtil)
    {
        _logger = logger;

        var modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        Config = RaidRecoveryConfig.Load(modFolder, out _configWarning);

        // The mod lives in user/mods/RaidRecovery: we go up two levels to reach user/raid-recovery,
        // without depending on the current directory of the process.
        var userFolder = Path.GetFullPath(Path.Combine(modFolder, "..", ".."));
        StorageDirectory = Path.Combine(userFolder, "raid-recovery");

        Service = new RaidRecoveryService(new SnapshotStore(StorageDirectory), TimeProvider.System, Config.MaxAge);
        Loot = new LootReplayService(
            new LootStore(StorageDirectory, loot => jsonUtil.Serialize(loot), json => jsonUtil.Deserialize<StoredLoot>(json)),
            // What the template does not declare (body shape, bones) lands in its extension data and leaves as it came
            json => jsonUtil.Deserialize<SpawnpointTemplate>(json)
        );
        Weather = new WeatherReplayService(
            new WeatherStore(
                StorageDirectory,
                weather => jsonUtil.Serialize(weather),
                json => jsonUtil.Deserialize<GetLocalWeatherResponseData>(json)
            )
        );
    }

    public RaidRecoveryService Service { get; }

    public LootReplayService Loot { get; }

    public WeatherReplayService Weather { get; }

    public RaidRecoveryConfig Config { get; }

    public string StorageDirectory { get; }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (_configWarning is not null)
        {
            _logger.Warning($"[RaidRecovery] {_configWarning}");
        }

        try
        {
            LootGeneratedPatch.Service = Loot;
            LootGeneratedPatch.Recovery = Service;
            LootGeneratedPatch.Logger = _logger;
            new LootGeneratedPatch().Enable();
        }
        catch (Exception ex)
        {
            // Without the patch, the rest of the mod still works: a resumed raid just gets new loot
            _logger.Error("[RaidRecovery] Loot replay unavailable", ex);
        }

        try
        {
            WeatherGeneratedPatch.Service = Weather;
            WeatherGeneratedPatch.Logger = _logger;
            new WeatherGeneratedPatch().Enable();
        }
        catch (Exception ex)
        {
            _logger.Error("[RaidRecovery] Weather replay unavailable", ex);
        }

        _logger.Success($"[RaidRecovery] Loaded. Snapshots in {StorageDirectory}, expiry {Config.MaxAge.TotalHours:0} h, "
                + $"resumes per raid {(Config.ResumeLimit == 0 ? "unlimited" : Config.ResumeLimit.ToString())}, "
                + $"blocked under {Config.VitalHealthFloor} % of vital health"
        );
        return Task.CompletedTask;
    }
}
