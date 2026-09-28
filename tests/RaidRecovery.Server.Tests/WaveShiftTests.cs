using RaidRecovery.Server.Services;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Tests;

public class WaveShiftTests
{
    private static LocationBase Map()
    {
        return new LocationBase
        {
            Id = "factory4_day",
            Waves =
            [
                new Wave { Number = 0, TimeMin = 0, TimeMax = 60, SlotsMin = 1 },
                new Wave { Number = 1, TimeMin = 300, TimeMax = 420, SlotsMin = 2 },
                new Wave { Number = 2, TimeMin = 900, TimeMax = 1000, SlotsMin = 1 },
            ],
            BossLocationSpawn =
            [
                new BossLocationSpawn { BossName = "atStart", Time = -1, BossEscortAmount = "0" },
                new BossLocationSpawn { BossName = "early", Time = 120, BossEscortAmount = "1,2,2" },
                new BossLocationSpawn { BossName = "late", Time = 1200, BossEscortAmount = "0" },
                new BossLocationSpawn { BossName = "onSwitch", Time = -1, TriggerId = "switch-01", TriggerName = "interactObject" },
            ],
        };
    }

    private static List<string?> Bosses(LocationBase map) => map.BossLocationSpawn!.Select(boss => boss.BossName).ToList();

    [Fact]
    public void Past_spawns_are_removed_when_the_snapshot_holds_their_bots()
    {
        var map = Map();

        // atStart 1 bot, early 1 + 1 escort, wave 0 one bot, wave 1 two bots: 6 bots
        var result = WaveShift.Apply(map, 600, botsInSnapshot: 6);

        Assert.Equal(4, result.Removed);
        Assert.Equal(0, result.Replayed);
        Assert.Equal(["late", "onSwitch"], Bosses(map));
        Assert.Equal([2], map.Waves!.Select(wave => wave.Number));
    }

    [Fact]
    public void A_past_spawn_nobody_stands_for_is_played_again_at_once()
    {
        var map = Map();

        // The case seen in game: 8 spawns due, 2 bots in the snapshot. Dropping the 8 emptied the map.
        var result = WaveShift.Apply(map, 600, botsInSnapshot: 1);

        Assert.Equal(1, result.Removed);
        Assert.Equal(3, result.Replayed);
        Assert.Equal(["early", "late", "onSwitch"], Bosses(map));
        Assert.Equal(-1, map.BossLocationSpawn!.Single(boss => boss.BossName == "early").Time);
        Assert.Equal([0, 0, 300], map.Waves!.Select(wave => wave.TimeMin));
    }

    [Fact]
    public void A_wave_played_again_keeps_its_length()
    {
        var map = Map();

        WaveShift.Apply(map, 600, botsInSnapshot: 0);

        var wave = map.Waves!.Single(item => item.Number == 1);
        Assert.Equal(0, wave.TimeMin);
        Assert.Equal(120, wave.TimeMax);
    }

    [Fact]
    public void An_empty_snapshot_removes_nothing()
    {
        var map = Map();

        var result = WaveShift.Apply(map, 600, botsInSnapshot: 0);

        Assert.Equal(0, result.Removed);
        Assert.Equal(4, map.BossLocationSpawn!.Count);
        Assert.Equal(3, map.Waves!.Count);
    }

    [Fact]
    public void A_spawn_too_big_for_the_bots_left_is_kept()
    {
        var map = Map();

        // atStart takes one bot, one is left: not enough for early and its escort
        WaveShift.Apply(map, 600, botsInSnapshot: 2);

        Assert.Contains("early", Bosses(map));
        Assert.DoesNotContain("atStart", Bosses(map));
    }

    [Fact]
    public void Spawns_to_come_are_brought_closer_by_the_time_played()
    {
        var map = Map();

        var result = WaveShift.Apply(map, 600, botsInSnapshot: 6);

        Assert.Equal(2, result.Shifted);
        var wave = Assert.Single(map.Waves!);
        Assert.Equal(300, wave.TimeMin);
        Assert.Equal(400, wave.TimeMax);
        Assert.Equal(600, map.BossLocationSpawn!.Single(boss => boss.BossName == "late").Time);
    }

    [Fact]
    public void A_spawn_waiting_for_an_action_of_the_player_is_left_alone()
    {
        var map = Map();

        WaveShift.Apply(map, 600, botsInSnapshot: 50);

        var boss = map.BossLocationSpawn!.Single(spawn => spawn.BossName == "onSwitch");
        Assert.Equal(-1, boss.Time);
    }

    [Fact]
    public void A_raid_resumed_at_its_very_start_keeps_all_its_spawns()
    {
        var map = Map();

        var result = WaveShift.Apply(map, 0, botsInSnapshot: 6);

        Assert.Equal(0, result.Removed);
        Assert.Equal(3, map.Waves!.Count);
        Assert.Equal(4, map.BossLocationSpawn!.Count);
        Assert.Equal([0, 300, 900], map.Waves!.Select(wave => wave.TimeMin));
    }

    [Fact]
    public void A_negative_time_played_changes_nothing()
    {
        var map = Map();

        WaveShift.Apply(map, -30, botsInSnapshot: 6);

        Assert.Equal([0, 300, 900], map.Waves!.Select(wave => wave.TimeMin));
    }

    [Fact]
    public void A_map_without_spawns_does_not_fail()
    {
        var result = WaveShift.Apply(new LocationBase { Id = "factory4_day" }, 600, botsInSnapshot: 3);

        Assert.Equal(new WaveShiftResult(0, 0, 0), result);
    }
}
