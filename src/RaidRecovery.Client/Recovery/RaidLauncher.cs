using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JsonType;
using Newtonsoft.Json.Linq;
using RaidRecovery.Client.Api;
using RaidRecovery.Client.Coop;
using RaidRecovery.Client.Teammates;
using SPT.Common.Http;
using SPT.Reflection.Utils;

namespace RaidRecovery.Client.Recovery
{
    /// <summary>The two calls to the game that the recovery needs: reload the menu, launch a raid.</summary>
    internal static class RaidLauncher
    {
        private const string DefaultRaidSettingsRoute = "/singleplayer/settings/raid/menu";

        public static void ReloadMenu()
        {
            var app = ClientAppUtils.GetMainApp() ?? throw new InvalidOperationException("Game application not found");
            Watch(app.ComebackToMainMenu(), "Menu reload");
        }

        public const string ScavSide = "Savage";

        public static bool IsScav(string side)
        {
            return string.Equals(side, ScavSide, StringComparison.OrdinalIgnoreCase);
        }

        /// <param name="raid">The raid as the coordinator knows it. null for a raid played alone.</param>
        /// <param name="onFailure">Called if the raid played with others cannot be launched after all.</param>
        /// <param name="squad">Teammates a teammate mod brings back, as the snapshot holds them. null without one.</param>
        public static void Start(string map, string dateTime, string side, List<string> squad, InterruptedRaid raid, ResumeWay way, Action<Exception> onFailure)
        {
            var app = ClientAppUtils.GetMainApp() ?? throw new InvalidOperationException("Game application not found");

            // InternalStartGame looks the map up with "contains": we first make sure it exists under this exact name
            var location = app.Session.LocationSettings.locations.Values.FirstOrDefault(l =>
                string.Equals(l.Id, map, StringComparison.OrdinalIgnoreCase)
            );
            if (location == null)
            {
                throw new InvalidOperationException($"Map unknown to the game: {map}");
            }

            var settings = app._raidSettings;
            // The raid is relaunched with the character it was played with: the menu may be set on the other one
            settings.Side = IsScav(side) ? EFT.ESideType.Savage : EFT.ESideType.Pmc;
            if (Enum.TryParse(dateTime, out EDateTime slot))
            {
                settings.SelectedDateTime = slot;
            }

            ApplyDefaultRaidSettings(settings);
            TeammateMods.BeforeLaunch(map, IsScav(side), squad);

            var coordinator = CoopGuard.Coordinator;
            if (coordinator == null)
            {
                Watch(app.InternalStartGame(map, true, true), "Raid launch");
                return;
            }

            var launch = StartWithAsync(coordinator, app, settings, location, map, raid, way);
            Watch(launch, "Raid launch");
            launch.ContinueWith(t => onFailure?.Invoke(t.Exception.GetBaseException()), TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// A raid played with others is announced before it is launched. The map is set here because the
        /// coordinator reads it from the settings, and the game only sets it once the launch has begun.
        /// </summary>
        private static async Task StartWithAsync(
            ICoopCoordinator coordinator,
            EFT.TarkovApplication app,
            EFT.RaidSettings settings,
            LocationSettings.Location location,
            string map,
            InterruptedRaid raid,
            ResumeWay way
        )
        {
            settings.SelectedLocation = location;
            // No ConfigureAwait(false): what follows calls the game, which only answers on the main thread
            await coordinator.BeforeLaunchAsync(raid ?? new InterruptedRaid { Map = map }, settings, way);
            Plugin.Log.LogInfo($"{coordinator.Name} is ready, launching the raid");
            await app.InternalStartGame(map, true, true);
        }

        /// <summary>
        /// When going through the menu, SPT applies its raid settings (bots, bosses) on the preparation screen.
        /// We skip that screen, so we apply them ourselves, the way SetPreRaidSettingsScreenDefaultsPatch does.
        /// </summary>
        private static void ApplyDefaultRaidSettings(EFT.RaidSettings settings)
        {
            try
            {
                var defaults = JObject.Parse(RequestHandler.GetJson(DefaultRaidSettingsRoute));
                T Read<T>(string name, T fallback)
                {
                    var token = defaults.GetValue(name, StringComparison.OrdinalIgnoreCase);
                    return token == null ? fallback : token.ToObject<T>();
                }

                settings.WavesSettings.BotAmount = Read("aiAmount", settings.WavesSettings.BotAmount);
                settings.WavesSettings.BotDifficulty = Read("aiDifficulty", settings.WavesSettings.BotDifficulty);
                settings.WavesSettings.IsBosses = Read("bossEnabled", settings.WavesSettings.IsBosses);
                settings.WavesSettings.IsTaggedAndCursed = Read("taggedAndCursed", settings.WavesSettings.IsTaggedAndCursed);
                settings.TimeAndWeatherSettings.IsRandomWeather = Read("randomWeather", settings.TimeAndWeatherSettings.IsRandomWeather);
                // Never a random time: we want the time slot of the interrupted raid back
                settings.TimeAndWeatherSettings.IsRandomTime = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Default raid settings not applied: {ex.Message}");
            }
        }

        /// <summary>These game tasks are started without being awaited: we at least log their failure.</summary>
        internal static void Watch(Task task, string label)
        {
            task.ContinueWith(
                t => Plugin.Log.LogError($"{label} failed: {t.Exception?.GetBaseException()}"),
                TaskContinuationOptions.OnlyOnFaulted
            );
        }
    }
}
