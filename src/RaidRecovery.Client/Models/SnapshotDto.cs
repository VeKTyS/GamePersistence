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

        [JsonProperty("world")]
        public WorldDto World { get; set; } = new WorldDto();

        /// <summary>
        /// Bots alive at the snapshot. null when their capture is turned off: the server then leaves the
        /// spawns of the map as they are, instead of dropping those it believes we restore.
        /// </summary>
        [JsonProperty("bots")]
        public List<BotDto> Bots { get; set; }
    }

    internal sealed class BotDto
    {
        [JsonProperty("position")]
        public PositionDto Position { get; set; }

        [JsonProperty("rotation")]
        public RotationDto Rotation { get; set; }

        /// <summary>Values of the game's enumerations: EPlayerSide, WildSpawnType, BotDifficulty.</summary>
        [JsonProperty("side")]
        public int Side { get; set; }

        [JsonProperty("role")]
        public int Role { get; set; }

        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        /// <summary>true if the bot was after the player when the raid was cut.</summary>
        [JsonProperty("huntsPlayer")]
        public bool HuntsPlayer { get; set; }

        /// <summary>Group the bot belonged to. Bots that share it come back in one group. null: not recorded.</summary>
        [JsonProperty("group")]
        public int? Group { get; set; }

        /// <summary>Name of the zone of that group, the one its members patrol.</summary>
        [JsonProperty("zone")]
        public string Zone { get; set; }

        /// <summary>true if the bot led its group: a boss with its guards, or the leader of a scav squad.</summary>
        [JsonProperty("isBoss")]
        public bool IsBoss { get; set; }

        /// <summary>Whole profile of the bot, gear and health included, serialized by the game.</summary>
        [JsonProperty("profile")]
        public JRaw Profile { get; set; }
    }

    /// <summary>State of the map around the player. Numbers are the values of the game's own enumerations.</summary>
    internal sealed class WorldDto
    {
        /// <summary>Side of the map the player entered from. It decides which extractions are theirs.</summary>
        [JsonProperty("entryPoint")]
        public string EntryPoint { get; set; }

        /// <summary>Doors, switches and the like: identifier to EDoorState.</summary>
        [JsonProperty("objects")]
        public Dictionary<string, byte> Objects { get; set; } = new Dictionary<string, byte>();

        /// <summary>Extractions: name to EExfiltrationStatus.</summary>
        [JsonProperty("exfils")]
        public Dictionary<string, byte> Exfils { get; set; } = new Dictionary<string, byte>();

        /// <summary>Identifiers of the containers the player already searched.</summary>
        [JsonProperty("searched")]
        public List<string> Searched { get; set; } = new List<string>();

        /// <summary>
        /// Bodies on the map, in the format the game reads in the loot of a raid. The server adds them
        /// to the loot it serves again: the client has nothing to do at recovery.
        /// </summary>
        [JsonProperty("corpses", NullValueHandling = NullValueHandling.Ignore)]
        public List<JRaw> Corpses { get; set; }

        /// <summary>
        /// Loot items the game saw on the map earlier in the raid and no longer sees: taken, used up, merged
        /// into a stack. Absent when the reading failed: the server then only knows what the player carries.
        /// </summary>
        [JsonProperty("gone", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Gone { get; set; }

        /// <summary>Items lying where the map did not put them, in the format of the loot of a raid.</summary>
        [JsonProperty("loose", NullValueHandling = NullValueHandling.Ignore)]
        public List<JRaw> Loose { get; set; }
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

        [JsonProperty("secondsPlayed")]
        public int? SecondsPlayed { get; set; }

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

        /// <summary>Kills, experience and counters of the raid so far, serialized by the game.</summary>
        [JsonProperty("stats", NullValueHandling = NullValueHandling.Ignore)]
        public JRaw Stats { get; set; }

        [JsonProperty("stance", NullValueHandling = NullValueHandling.Ignore)]
        public StanceDto Stance { get; set; }
    }

    /// <summary>How the player stood: posture, breath, and what they held.</summary>
    internal sealed class StanceDto
    {
        /// <summary>Height of the character, from 0 (crouched) to 1 (standing).</summary>
        [JsonProperty("poseLevel")]
        public float PoseLevel { get; set; } = 1f;

        [JsonProperty("prone")]
        public bool Prone { get; set; }

        [JsonProperty("stamina")]
        public float? Stamina { get; set; }

        [JsonProperty("handsStamina")]
        public float? HandsStamina { get; set; }

        [JsonProperty("oxygen")]
        public float? Oxygen { get; set; }

        /// <summary>Identifier of the item held, absent with empty hands.</summary>
        [JsonProperty("inHands", NullValueHandling = NullValueHandling.Ignore)]
        public string InHands { get; set; }
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

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("side")]
        public string Side { get; set; }
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

        [JsonProperty("world")]
        public WorldDto World { get; set; }

        [JsonProperty("bots")]
        public List<BotDto> Bots { get; set; }

        [JsonProperty("stats")]
        public JRaw Stats { get; set; }

        [JsonProperty("side")]
        public string Side { get; set; }

        [JsonProperty("stance")]
        public StanceDto Stance { get; set; }
    }
}
