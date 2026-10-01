using RaidRecovery.Client.Teammates;

namespace RaidRecovery.Server.Tests;

public class SquadPolicyTests
{
    [Fact]
    public void A_teammate_mod_is_found_among_the_plugins_whatever_its_case()
    {
        Assert.True(SquadPolicy.IsAmong(["com.SPT.core", "XYZ.Pit.Fireteam"], SquadPolicy.PitFireteamId));
    }

    [Fact]
    public void Without_the_teammate_mod_nothing_is_found()
    {
        Assert.False(SquadPolicy.IsAmong(["com.SPT.core", "com.vektys.raidrecovery"], SquadPolicy.PitFireteamId));
        Assert.False(SquadPolicy.IsAmong(null!, SquadPolicy.MiyakoId));
    }

    [Fact]
    public void Only_the_teammates_still_alive_next_to_the_player_come_back()
    {
        var squad = SquadPolicy.ToBringBack(["alice", "bob", "carol"], ["carol", "recruited scav", "alice"]);

        Assert.Equal(["alice", "carol"], squad);
    }

    [Fact]
    public void A_squad_with_no_one_left_alive_is_not_recorded()
    {
        Assert.Null(SquadPolicy.ToBringBack(["alice", "bob"], ["recruited scav"]));
        Assert.Null(SquadPolicy.ToBringBack(["alice"], []));
    }

    [Fact]
    public void A_teammate_listed_twice_comes_back_once()
    {
        // PIT keeps a PMC list and a scav list: the same teammate may be in both
        var squad = SquadPolicy.ToBringBack(["alice", "", "alice", null!], ["alice", null!]);

        Assert.Equal(["alice"], squad);
    }

    [Fact]
    public void Teammates_already_in_the_mod_list_are_not_added_again()
    {
        var target = new List<string> { "alice" };

        var added = SquadPolicy.AddMissing(target, ["alice", "bob"]);

        Assert.Equal(1, added);
        Assert.Equal(["alice", "bob"], target);
    }

    [Fact]
    public void The_squad_only_goes_to_the_raid_of_its_map()
    {
        Assert.True(SquadPolicy.IsRaidOf("bigmap", "BigMap"));
        Assert.False(SquadPolicy.IsRaidOf("bigmap", "factory4_day"));
        Assert.False(SquadPolicy.IsRaidOf(null!, "bigmap"));
    }
}
