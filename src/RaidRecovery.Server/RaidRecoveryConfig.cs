using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaidRecovery.Server;

public record RaidRecoveryConfig
{
    public const int DefaultMaxAgeHours = 24;
    public const int MinMaxAgeHours = 1;
    public const int MaxMaxAgeHours = 168;

    [JsonPropertyName("maxAgeHours")]
    public int MaxAgeHours { get; init; } = DefaultMaxAgeHours;

    [JsonIgnore]
    public TimeSpan MaxAge => TimeSpan.FromHours(Math.Clamp(MaxAgeHours, MinMaxAgeHours, MaxMaxAgeHours));

    /// <summary>
    /// Reads config.json in the mod folder. A missing or invalid file must not prevent
    /// the server from starting: we fall back to the defaults and report the reason.
    /// </summary>
    public static RaidRecoveryConfig Load(string modFolder, out string? warning)
    {
        warning = null;
        var path = Path.Combine(modFolder, "config.json");
        if (!File.Exists(path))
        {
            warning = "config.json not found, using default values";
            return new RaidRecoveryConfig();
        }

        try
        {
            var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            return JsonSerializer.Deserialize<RaidRecoveryConfig>(File.ReadAllText(path), options) ?? new RaidRecoveryConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            warning = $"config.json unreadable ({ex.Message}), using default values";
            return new RaidRecoveryConfig();
        }
    }
}
