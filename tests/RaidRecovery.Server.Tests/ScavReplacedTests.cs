using RaidRecovery.Server.Services;

namespace RaidRecovery.Server.Tests;

public class ScavReplacedTests
{
    [Fact]
    public void The_same_gear_is_the_same_scav()
    {
        Assert.False(ProfileRestorer.IsAnotherScav("6abc2a371155101148eeabb9", "6abc2a371155101148eeabb9"));
    }

    [Fact]
    public void The_identifier_is_compared_whatever_its_case()
    {
        Assert.False(ProfileRestorer.IsAnotherScav("6ABC2A371155101148EEABB9", "6abc2a371155101148eeabb9"));
    }

    [Fact]
    public void Another_gear_is_a_scav_made_since_the_snapshot()
    {
        Assert.True(ProfileRestorer.IsAnotherScav("6abc2a371155101148eeabb9", "6abbf11c7799ab5110fdf300"));
    }

    [Fact]
    public void A_profile_without_gear_takes_the_one_of_the_snapshot()
    {
        Assert.True(ProfileRestorer.IsAnotherScav(null, "6abbf11c7799ab5110fdf300"));
    }

    [Fact]
    public void A_snapshot_without_gear_replaces_nothing()
    {
        Assert.False(ProfileRestorer.IsAnotherScav("6abc2a371155101148eeabb9", null));
        Assert.False(ProfileRestorer.IsAnotherScav("6abc2a371155101148eeabb9", ""));
    }
}
