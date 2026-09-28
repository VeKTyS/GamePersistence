using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Services;

/// <param name="Removed">Spawns dropped: their bots are in the snapshot.</param>
/// <param name="Replayed">Spawns already due that are played again at once: nothing in the snapshot stands for them.</param>
/// <param name="Shifted">Spawns still to come, brought closer by the time already played.</param>
public sealed record WaveShiftResult(int Removed, int Replayed, int Shifted);

/// <summary>
/// A resumed raid restarts its clock at zero, so the game would spawn again every bot of the first minutes,
/// on top of those we put back. We drop as many past spawns as the snapshot holds bots, alive or dead, and no
/// more: a spawn can be due without having produced anyone (it rolls a chance, it waits for room), and dropping
/// it on its time alone empties the map. Pure: only touches the map it is given.
/// </summary>
public static class WaveShift
{
    public static WaveShiftResult Apply(LocationBase location, int elapsedSeconds, int botsInSnapshot)
    {
        if (elapsedSeconds <= 0)
        {
            return new WaveShiftResult(0, 0, (location.Waves?.Count ?? 0) + (location.BossLocationSpawn?.Count ?? 0));
        }

        var removed = 0;
        var replayed = 0;
        var shifted = 0;
        var budget = Math.Max(0, botsInSnapshot);

        // Oldest first: the bots of the snapshot most likely came out of the earliest spawns
        var bosses = location.BossLocationSpawn ?? [];
        foreach (var boss in bosses.Where(boss => !IsTriggered(boss)).OrderBy(boss => boss.Time ?? -1).ToList())
        {
            var time = boss.Time ?? -1;
            if (time >= elapsedSeconds)
            {
                boss.Time = time - elapsedSeconds;
                shifted++;
                continue;
            }

            var size = 1 + EscortCount(boss.BossEscortAmount);
            if (size <= budget)
            {
                budget -= size;
                bosses.Remove(boss);
                removed++;
            }
            else
            {
                // -1 is the game's own value for "at the start of the raid"
                boss.Time = -1;
                replayed++;
            }
        }

        var waves = location.Waves ?? [];
        foreach (var wave in waves.OrderBy(wave => wave.TimeMin ?? 0).ToList())
        {
            var start = wave.TimeMin ?? 0;
            var length = Math.Max(0, (wave.TimeMax ?? start) - start);
            if (start >= elapsedSeconds)
            {
                wave.TimeMin = start - elapsedSeconds;
                wave.TimeMax = wave.TimeMin + length;
                shifted++;
                continue;
            }

            var size = Math.Max(1, wave.SlotsMin ?? 1);
            if (size <= budget)
            {
                budget -= size;
                waves.Remove(wave);
                removed++;
            }
            else
            {
                wave.TimeMin = 0;
                wave.TimeMax = length;
                replayed++;
            }
        }

        return new WaveShiftResult(removed, replayed, shifted);
    }

    /// <summary>A spawn waiting for an action of the player (a switch, a door) has no time: it is left alone.</summary>
    private static bool IsTriggered(BossLocationSpawn boss)
    {
        return !string.IsNullOrEmpty(boss.TriggerId) || !string.IsNullOrEmpty(boss.TriggerName);
    }

    /// <summary>
    /// The map writes the escort as "2" or as a draw among "1,2,2". We count the smallest: dropping a spawn
    /// for bots that never existed costs more than playing one spawn too many.
    /// </summary>
    private static int EscortCount(string? amount)
    {
        if (string.IsNullOrWhiteSpace(amount))
        {
            return 0;
        }

        var counts = amount
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var count) ? count : 0)
            .ToList();
        return counts.Count == 0 ? 0 : Math.Max(0, counts.Min());
    }
}
