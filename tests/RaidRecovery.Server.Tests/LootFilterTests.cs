using RaidRecovery.Server.Services;

namespace RaidRecovery.Server.Tests;

public class LootFilterTests
{
    private static HashSet<string> Taken(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Nothing_taken_leaves_the_loot_as_it_was()
    {
        var result = LootFilter.WithoutTaken(Loot.Sample(), Taken(), out var removed);

        Assert.Equal(0, removed);
        Assert.Equal(Loot.Ids(Loot.Sample()), Loot.Ids(result));
    }

    [Fact]
    public void An_item_taken_from_a_crate_leaves_the_crate_and_the_rest_of_its_content()
    {
        var result = LootFilter.WithoutTaken(Loot.Sample(), Taken(Loot.Bandage), out var removed);

        Assert.Equal(1, removed);
        var crate = Assert.Single(result, spawnpoint => spawnpoint.Id == "crate");
        Assert.Equal([Loot.Crate, Loot.Bolts], crate.Items!.Select(item => item.Id.ToString()));
    }

    [Fact]
    public void An_item_taken_from_the_ground_removes_its_whole_spawn_point()
    {
        var result = LootFilter.WithoutTaken(Loot.Sample(), Taken(Loot.Wrench), out var removed);

        Assert.Equal(1, removed);
        Assert.DoesNotContain(result, spawnpoint => spawnpoint.Id == "wrench");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void A_taken_item_leaves_with_everything_it_holds()
    {
        var result = LootFilter.WithoutTaken(Loot.Sample(), Taken(Loot.Rifle), out var removed);

        // Rifle, magazine and round: the snapshot only had to name the rifle
        Assert.Equal(3, removed);
        Assert.DoesNotContain(result, spawnpoint => spawnpoint.Id == "rifle");
    }

    [Fact]
    public void A_part_taken_off_an_item_leaves_the_item_in_place()
    {
        var result = LootFilter.WithoutTaken(Loot.Sample(), Taken(Loot.Magazine), out var removed);

        Assert.Equal(2, removed);
        var rifle = Assert.Single(result, spawnpoint => spawnpoint.Id == "rifle");
        Assert.Equal([Loot.Rifle], rifle.Items!.Select(item => item.Id.ToString()));
    }

    [Fact]
    public void Identifiers_that_match_nothing_are_ignored()
    {
        var result = LootFilter.WithoutTaken(Loot.Sample(), Taken("f00000000000000000000001"), out var removed);

        Assert.Equal(0, removed);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void The_loot_given_as_input_is_not_modified()
    {
        var loot = Loot.Sample();

        LootFilter.WithoutTaken(loot, Taken(Loot.Bandage, Loot.Rifle), out _);

        Assert.Equal(Loot.Ids(Loot.Sample()), Loot.Ids(loot));
    }
}
