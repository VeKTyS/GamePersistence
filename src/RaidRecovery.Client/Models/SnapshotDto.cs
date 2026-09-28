using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RaidRecovery.Client.Models
{
    /// <summary>
    /// Snapshot sent to the server. profileId and savedAt are not in it: the server sets them,
    /// from the session and its own clock.
    /// </summary>
    internal sealed class SnapshotDto
    {
        public const int CurrentVersion = 1;

        [JsonProperty("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("map")]
        public string Map { get; set; }

        [JsonProperty("raid")]
        public RaidDto Raid { get; set; }

        [JsonProperty("player")]
        public PlayerDto Player { get; set; }

        // Filled starting with milestones L2 and L3
        [JsonProperty("world")]
        public Dictionary<string, object> World { get; set; } = new Dictionary<string, object>();

        [JsonProperty("bots")]
        public List<object> Bots { get; set; } = new List<object>();
    }

    internal sealed class RaidDto
    {
        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("dateTime")]
        public string DateTime { get; set; }

        [JsonProperty("gameTime")]
        public string GameTime { get; set; }

        [JsonProperty("secondsLeft")]
        public int? SecondsLeft { get; set; }

        [JsonProperty("side")]
        public string Side { get; set; }
    }

    internal sealed class PlayerDto
    {
        [JsonProperty("position")]
        public PositionDto Position { get; set; }

        [JsonProperty("rotation")]
        public RotationDto Rotation { get; set; }

        /// <summary>
        /// Inventory and health, already serialized by the game. JRaw inserts it as is in the JSON,
        /// without our serializer reading or reformatting it.
        /// </summary>
        [JsonProperty("profile")]
        public JRaw Profile { get; set; }
    }

    internal sealed class PositionDto
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("z")]
        public float Z { get; set; }
    }

    internal sealed class RotationDto
    {
        [JsonProperty("yaw")]
        public float Yaw { get; set; }

        [JsonProperty("pitch")]
        public float Pitch { get; set; }
    }

    /// <summary>Common envelope of SPT server responses: { err, errmsg, data }.</summary>
    internal sealed class ServerResponse<T>
    {
        [JsonProperty("err")]
        public int Err { get; set; }

        [JsonProperty("errmsg")]
        public string ErrMsg { get; set; }

        [JsonProperty("data")]
        public T Data { get; set; }
    }

    internal sealed class SaveResult
    {
        [JsonProperty("saved")]
        public bool Saved { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }

    internal sealed class PendingResult
    {
        [JsonProperty("pending")]
        public bool Pending { get; set; }

        [JsonProperty("map")]
        public string Map { get; set; }

        [JsonProperty("secondsLeft")]
        public int? SecondsLeft { get; set; }

        [JsonProperty("savedAt")]
        public DateTimeOffset? SavedAt { get; set; }

        [JsonProperty("restorable")]
        public bool Restorable { get; set; }
    }

    internal sealed class RestoreResult
    {
        [JsonProperty("restored")]
        public bool Restored { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("map")]
        public string Map { get; set; }

        [JsonProperty("dateTime")]
        public string DateTime { get; set; }

        [JsonProperty("secondsLeft")]
        public int? SecondsLeft { get; set; }

        [JsonProperty("position")]
        public PositionDto Position { get; set; }

        [JsonProperty("rotation")]
        public RotationDto Rotation { get; set; }
    }
}
