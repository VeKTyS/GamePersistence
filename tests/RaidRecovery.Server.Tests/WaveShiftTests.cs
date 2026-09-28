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
                new Wave { Number = 0, TimeMin = 0, TimeMax = 60 },
                new Wave { Number = 1, TimeMin = 300, TimeMax = 420 },
                new Wave { Number = 2, TimeMin = 900, TimeMax = 1000 },
            ],
            BossLocationSpawn =
            [
                new BossLocationSpawn { BossName = "atStart", Time = -1 },
                new BossLocationSpawn { BossName = "early", Time = 120 },
                new BossLocationSpawn { BossName = "late", Time = 1200 },
                new BossLocationSpawn { BossName = "onSwitch", Time = -1, TriggerId = "switch-01", TriggerName = "interactObject" },
            ],
        };
    }

    [Fact]
    public void Spawns_already_played_are_removed()
    {
        var map = Map();

        var result = WaveShift.Apply(map, 600);

        Assert.Equal(2, result.WavesRemoved);
        Assert.Equal([2], map.Waves!.Select(wave => wave.Number));
        Assert.Equal(2, result.BossSpawnsRemoved);
        Assert.Equal(["late", "onSwitch"], map.BossLocationSpawn!.Select(boss => boss.BossName));
    }

    [Fact]
    public void Spawns_to_come_are_brought_closer_by_the_time_played()
    {
        var map = Map();

        WaveShift.Apply(map, 600);

        var wave = Assert.Single(map.Waves!);
        Assert.Equal(300, wave.TimeMin);
        Assert.Equal(400, wave.TimeMax);
        Assert.Equal(600, map.BossLocationSpawn!.Single(boss => boss.BossName == "late").Time);
    }

    [Fact]
    public void A_spawn_waiting_for_an_action_of_the_player_is_left_alone()
    {
        var map = Map();

        WaveShift.Apply(map, 600);

        var boss = map.BossLocationSpawn!.Single(spawn => spawn.BossName == "onSwitch");
        Assert.Equal(-1, boss.Time);
    }

    [Fact]
    public void A_raid_resumed_at_its_very_start_keeps_all_its_spawns()
    {
        var map = Map();

        var result = WaveShift.Apply(map, 0);

        Assert.Equal(0, result.WavesRemoved);
        Assert.Equal(0, result.BossSpawnsRemoved);
        Assert.Equal(3, map.Waves!.Count);
        Assert.Equal(4, map.BossLocationSpawn!.Count);
    }

    [Fact]
    public void A_negative_time_played_changes_nothing()
    {
        var map = Map();

        WaveShift.Apply(map, -30);

        Assert.Equal([0, 300, 900], map.Waves!.Select(wave => wave.TimeMin));
    }

    [Fact]
    public void A_map_without_spawns_does_not_fail()
    {
        var result = WaveShift.Apply(new LocationBase { Id = "factory4_day" }, 600);

        Assert.Equal(new WaveShiftResult(0, 0, 0, 0), result);
    }
}
