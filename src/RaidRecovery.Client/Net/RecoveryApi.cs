using System.Threading.Tasks;
using Newtonsoft.Json;
using RaidRecovery.Client.Models;
using SPT.Common.Http;

namespace RaidRecovery.Client.Net
{
    /// <summary>
    /// Calls to the server mod's routes. We go through SPT's RequestHandler: it already carries
    /// the server address, the session and the expected compression.
    /// </summary>
    internal static class RecoveryApi
    {
        private const string SaveRoute = "/raid-recovery/save";
        private const string PendingRoute = "/raid-recovery/pending";
        private const string RestoreRoute = "/raid-recovery/restore";
        private const string DiscardRoute = "/raid-recovery/discard";
        private const string ResumedRoute = "/raid-recovery/resumed";

        private const string EmptyBody = "{}";

        public static async Task<SaveResult> SaveAsync(string snapshotJson)
        {
            var json = await RequestHandler.PostJsonAsync(SaveRoute, snapshotJson).ConfigureAwait(false);
            return Read<SaveResult>(json);
        }

        public static async Task<PendingResult> GetPendingAsync()
        {
            var json = await RequestHandler.PostJsonAsync(PendingRoute, EmptyBody).ConfigureAwait(false);
            return Read<PendingResult>(json);
        }

        public static async Task<RestoreResult> RestoreAsync()
        {
            var json = await RequestHandler.PostJsonAsync(RestoreRoute, EmptyBody).ConfigureAwait(false);
            return Read<RestoreResult>(json);
        }

        public static async Task<ResumedResult> GetResumedAsync(string profileId)
        {
            var body = JsonConvert.SerializeObject(new ResumedQuery { ProfileId = profileId });
            var json = await RequestHandler.PostJsonAsync(ResumedRoute, body).ConfigureAwait(false);
            return Read<ResumedResult>(json);
        }

        public static Task DiscardAsync()
        {
            return RequestHandler.PostJsonAsync(DiscardRoute, EmptyBody);
        }

        private static T Read<T>(string json)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<ServerResponse<T>>(json)?.Data;
        }
    }
}
