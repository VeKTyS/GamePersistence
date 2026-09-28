using System;
using System.Linq;
using System.Threading.Tasks;
using JsonType;
using Newtonsoft.Json.Linq;
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

        public static void Start(string map, string dateTime)
        {
            var app = ClientAppUtils.GetMainApp() ?? throw new InvalidOperationException("Game application not found");

            // InternalStartGame looks the map up with "contains": we first make sure it exists under this exact name
            var known = app.Session.LocationSettings.locations.Values.Any(l => string.Equals(l.Id, map, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                throw new InvalidOperationException($"Map unknown to the game: {map}");
            }

            var settings = app._raidSettings;
            if (Enum.TryParse(dateTime, out EDateTime slot))
            {
                settings.SelectedDateTime = slot;
            }

            ApplyDefaultRaidSettings(settings);
            Watch(app.InternalStartGame(map, true, true), "Raid launch");
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
