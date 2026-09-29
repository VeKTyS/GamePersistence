using RaidRecovery.Client.Coop;

namespace RaidRecovery.Server.Tests;

public class CoopPolicyTests
{
    private const string Fika = "com.fika.core";

    [Fact]
    public void Solo_plugins_do_not_make_the_mod_stand_down()
    {
        var found = CoopPolicy.CoopModAmong(["com.SPT.core", "com.SPT.custom", "com.vektys.raidrecovery"]);

        Assert.Null(found);
    }

    [Fact]
    public void Fika_makes_the_mod_stand_down()
    {
        var found = CoopPolicy.CoopModAmong(["com.SPT.core", "com.fika.core", "com.vektys.raidrecovery"]);

        Assert.Equal("com.fika.core", found);
    }

    [Fact]
    public void The_identifier_is_matched_whatever_its_case()
    {
        var found = CoopPolicy.CoopModAmong(["com.Fika.Core"]);

        Assert.Equal("com.Fika.Core", found);
    }

    [Fact]
    public void A_mod_made_for_fika_is_not_fika()
    {
        // Compatibility DLLs shipped by other mods carry "fika" in their name without the co-op being installed
        var found = CoopPolicy.CoopModAmong(["com.lacyway.mergeconsumables.fika", "com.fika.core.addon"]);

        Assert.Null(found);
    }

    [Fact]
    public void No_plugin_list_means_solo()
    {
        Assert.Null(CoopPolicy.CoopModAmong(null));
        Assert.Null(CoopPolicy.CoopModAmong([]));
    }

    [Fact]
    public void Alone_the_mod_never_stands_down_and_always_saves()
    {
        Assert.False(CoopPolicy.StandsDown(null, coordinatorDeclared: false));
        Assert.True(CoopPolicy.SavesRaid(null, coordinatorDeclared: false, coordinatorSaves: false));
    }

    [Fact]
    public void A_coordinator_left_installed_without_the_coop_mod_changes_nothing()
    {
        Assert.False(CoopPolicy.StandsDown(null, coordinatorDeclared: true));
        Assert.True(CoopPolicy.SavesRaid(null, coordinatorDeclared: true, coordinatorSaves: false));
    }

    [Fact]
    public void With_fika_and_no_coordinator_nothing_is_saved()
    {
        Assert.True(CoopPolicy.StandsDown(Fika, coordinatorDeclared: false));
        Assert.False(CoopPolicy.SavesRaid(Fika, coordinatorDeclared: false, coordinatorSaves: true));
    }

    [Fact]
    public void With_a_coordinator_the_host_saves_the_raid()
    {
        Assert.False(CoopPolicy.StandsDown(Fika, coordinatorDeclared: true));
        Assert.True(CoopPolicy.SavesRaid(Fika, coordinatorDeclared: true, coordinatorSaves: true));
    }

    [Fact]
    public void The_player_who_holds_the_world_saves_at_once_then_follows_the_clock()
    {
        Assert.True(CoopPolicy.IsDue(holdsTheWorld: true, requested: false, capturedOnce: false, now: 5f, lastCaptureAt: 0f, interval: 30f));
        Assert.False(CoopPolicy.IsDue(holdsTheWorld: true, requested: false, capturedOnce: true, now: 29f, lastCaptureAt: 0f, interval: 30f));
        Assert.True(CoopPolicy.IsDue(holdsTheWorld: true, requested: false, capturedOnce: true, now: 30f, lastCaptureAt: 0f, interval: 30f));
    }

    [Fact]
    public void A_player_who_joined_only_saves_when_asked()
    {
        Assert.False(CoopPolicy.IsDue(holdsTheWorld: false, requested: false, capturedOnce: false, now: 5f, lastCaptureAt: 0f, interval: 30f));
        Assert.False(CoopPolicy.IsDue(holdsTheWorld: false, requested: false, capturedOnce: true, now: 900f, lastCaptureAt: 0f, interval: 30f));
        Assert.True(CoopPolicy.IsDue(holdsTheWorld: false, requested: true, capturedOnce: true, now: 1f, lastCaptureAt: 0f, interval: 30f));
    }

    [Fact]
    public void A_snapshot_asked_by_hand_does_not_wait_for_the_clock()
    {
        Assert.True(CoopPolicy.IsDue(holdsTheWorld: true, requested: true, capturedOnce: true, now: 1f, lastCaptureAt: 0f, interval: 30f));
    }

    [Fact]
    public void With_a_coordinator_a_player_who_joined_does_not_save_the_raid()
    {
        Assert.False(CoopPolicy.StandsDown(Fika, coordinatorDeclared: true));
        Assert.False(CoopPolicy.SavesRaid(Fika, coordinatorDeclared: true, coordinatorSaves: false));
    }
}
