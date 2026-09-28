using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Services;

public sealed record WaveShiftResult(int WavesRemoved, int WavesKept, int BossSpawnsRemoved, int BossSpawnsKept);

/// <summary>
/// A resumed raid restarts its clock at zero, so the game would spawn again every bot of the first minutes.
/// Those bots are in the snapshot, alive or dead. We drop the spawns that already happened and bring the
/// others closer by the time already played. Pure: only touches the map it is given.
/// </summary>
public static class WaveShift
{
    public static WaveShiftResult Apply(LocationBase location, int elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
        {
            return new WaveShiftResult(0, location.Waves?.Count ?? 0, 0, location.BossLocationSpawn?.Count ?? 0);
        }

        var wavesRemoved = 0;
        if (location.Waves is { } waves)
        {
            wavesRemoved = waves.RemoveAll(wave => (wave.TimeMin ?? 0) < elapsedSeconds);
            foreach (var wave in waves)
            {
                wave.TimeMin = (wave.TimeMin ?? 0) - elapsedSeconds;
                if (wave.TimeMax is { } max)
                {
                    wave.TimeMax = Math.Max(wave.TimeMin.Value, max - elapsedSeconds);
                }
            }
        }

        var bossesRemoved = 0;
        if (location.BossLocationSpawn is { } bosses)
        {
            bossesRemoved = bosses.RemoveAll(boss => !IsTriggered(boss) && (boss.Time ?? -1) < elapsedSeconds);
            foreach (var boss in bosses)
            {
                if (!IsTriggered(boss))
                {
                    boss.Time -= elapsedSeconds;
                }
            }
        }

        return new WaveShiftResult(wavesRemoved, location.Waves?.Count ?? 0, bossesRemoved, location.BossLocationSpawn?.Count ?? 0);
    }

    /// <summary>A spawn waiting for an action of the player (a switch, a door) has no time: it is left alone.</summary>
    private static bool IsTriggered(BossLocationSpawn boss)
    {
        return !string.IsNullOrEmpty(boss.TriggerId) || !string.IsNullOrEmpty(boss.TriggerName);
    }
}
