using RaidRecovery.Client.Coop;

namespace RaidRecovery.Server.Tests;

public class CoopPolicyTests
{
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
}
