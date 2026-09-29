using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaidRecovery.Server.Models;

public record SaveResponse
{
    [JsonPropertyName("saved")]
    public bool Saved { get; init; }

    /// <summary>Reason for the refusal, absent if the snapshot was written.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>Summary shown by the recovery window: we do not send the whole snapshot for a simple question.</summary>
public record PendingResponse
{
    [JsonPropertyName("pending")]
    public bool Pending { get; init; }

    [JsonPropertyName("map")]
    public string? Map { get; init; }

    [JsonPropertyName("secondsLeft")]
    public int? SecondsLeft { get; init; }

    [JsonPropertyName("savedAt")]
    public DateTimeOffset? SavedAt { get; init; }

    /// <summary>false if the snapshot does not hold what is needed to restore the character (capture from milestone L0, scav raid).</summary>
    [JsonPropertyName("restorable")]
    public bool Restorable { get; init; }

    /// <summary>Why the raid cannot be resumed, when a rule of the server forbids it.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>Pmc or Savage: the character the raid was played with.</summary>
    [JsonPropertyName("side")]
    public string? Side { get; init; }
}

/// <summary>
/// What the client needs to relaunch the raid. The inventory is not in it:
/// it was applied to the profile, which the client reloads the normal way.
/// </summary>
public record RestoreResponse
{
    [JsonPropertyName("restored")]
    public bool Restored { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("map")]
    public string? Map { get; init; }

    [JsonPropertyName("dateTime")]
    public string? DateTime { get; init; }

    [JsonPropertyName("secondsLeft")]
    public int? SecondsLeft { get; init; }

    [JsonPropertyName("position")]
    public Position? Position { get; init; }

    [JsonPropertyName("rotation")]
    public Rotation? Rotation { get; init; }

    /// <summary>Doors, extractions, searched containers: handed back as the game sent them, the server does not read them.</summary>
    [JsonPropertyName("world")]
    public JsonElement? World { get; init; }

    /// <summary>Bots alive at the snapshot, handed back as the game sent them.</summary>
    [JsonPropertyName("bots")]
    public JsonElement? Bots { get; init; }

    /// <summary>Kills, experience and counters of the raid so far, handed back as the game sent them.</summary>
    [JsonPropertyName("stats")]
    public JsonElement? Stats { get; init; }

    /// <summary>Pmc or Savage: the character the raid must be relaunched with.</summary>
    [JsonPropertyName("side")]
    public string? Side { get; init; }

    /// <summary>Posture, breath and item in hands, handed back as the game sent them.</summary>
    [JsonPropertyName("stance")]
    public JsonElement? Stance { get; init; }
}

public record DiscardResponse
{
    [JsonPropertyName("discarded")]
    public bool Discarded { get; init; }
}
