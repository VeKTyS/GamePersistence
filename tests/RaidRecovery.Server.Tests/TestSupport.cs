using System.Text.Json;
using RaidRecovery.Server.Models;

namespace RaidRecovery.Server.Tests;

/// <summary>Clock driven by the test: expiry can be checked without waiting 24 hours.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now += duration;
}

/// <summary>Throwaway folder per test, deleted at the end.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "raid-recovery-tests", Guid.NewGuid().ToString("N"));
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

public static class Samples
{
    public const string ProfileId = "6ab58e74984af4a498879c6d";
    public const string OtherProfileId = "aaaaaaaaaaaaaaaaaaaaaaaa";

    public static Snapshot Snapshot(string sessionId = "20260927131211", string map = "bigmap", int version = 1)
    {
        return new Snapshot
        {
            Version = version,
            SessionId = sessionId,
            Map = map,
            Raid = new RaidInfo { SecondsLeft = 1380, Side = "Pmc" },
        };
    }

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
