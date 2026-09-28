using System.Text.Json;
using RaidRecovery.Server.Models;

namespace RaidRecovery.Server.Services;

/// <summary>
/// The two rules the specification leaves to the player: how many times a raid may be resumed, and whether a
/// raid cut just before dying may be resumed at all. Both are off by default. Pure: reads what it is given.
/// </summary>
public static class ResumePolicy
{
    public const string TooManyResumes = "TooManyResumes";
    public const string DeathWasImminent = "DeathWasImminent";

    // The two parts whose loss kills the character
    private static readonly string[] VitalParts = ["Head", "Chest"];

    /// <returns>null if the raid may be resumed, otherwise the reason why it may not.</returns>
    public static string? Refusal(Snapshot snapshot, int resumesDone, RaidRecoveryConfig config)
    {
        if (config.ResumeLimit > 0 && resumesDone >= config.ResumeLimit)
        {
            return TooManyResumes;
        }

        if (config.VitalHealthFloor > 0 && LowestVitalPercent(snapshot) is { } lowest && lowest < config.VitalHealthFloor)
        {
            return DeathWasImminent;
        }

        return null;
    }

    /// <summary>
    /// Health of the weakest vital part, in percent of its maximum. null if the snapshot does not say:
    /// a rule that cannot be checked does not block a recovery.
    /// </summary>
    public static double? LowestVitalPercent(Snapshot snapshot)
    {
        if (
            snapshot.Player?.Profile is not { ValueKind: JsonValueKind.Object } profile
            || !profile.TryGetProperty("Health", out var health)
            || health.ValueKind != JsonValueKind.Object
            || !health.TryGetProperty("BodyParts", out var parts)
            || parts.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }

        double? lowest = null;
        foreach (var name in VitalParts)
        {
            if (
                !parts.TryGetProperty(name, out var part)
                || part.ValueKind != JsonValueKind.Object
                || !part.TryGetProperty("Health", out var points)
                || points.ValueKind != JsonValueKind.Object
                || !points.TryGetProperty("Current", out var current)
                || !points.TryGetProperty("Maximum", out var maximum)
                || current.ValueKind != JsonValueKind.Number
                || maximum.ValueKind != JsonValueKind.Number
                || maximum.GetDouble() <= 0
            )
            {
                continue;
            }

            // Rounded: a division leaves 9.999999999999998 where 10 is meant, and a health right at the floor must pass
            var percent = Math.Round(current.GetDouble() / maximum.GetDouble() * 100, 4);
            lowest = lowest is null ? percent : Math.Min(lowest.Value, percent);
        }

        return lowest;
    }
}
