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
}

public record DiscardResponse
{
    [JsonPropertyName("discarded")]
    public bool Discarded { get; init; }
}
