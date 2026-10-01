using RaidRecovery.Client.Recovery;

namespace RaidRecovery.Server.Tests;

/// <summary>The plugin has no test project of its own: the part of it that does not need the game is tested here.</summary>
public class GroupPlanTests
{
    private sealed record Bot(string Name, int? Group = null, bool Leader = false);

    private static readonly Bot[] Snapshot =
    [
        new("guard 1", Group: 7),
        new("lone scav", Group: 3),
        new("boss", Group: 7, Leader: true),
        new("guard 2", Group: 7),
        new("old snapshot"),
    ];

    [Fact]
    public void Leaders_come_back_before_their_followers()
    {
        var order = GroupPlan.LeadersFirst(Snapshot, bot => bot.Leader).Select(bot => bot.Name).ToList();

        Assert.Equal(["boss", "guard 1", "lone scav", "guard 2", "old snapshot"], order);
    }

    [Fact]
    public void A_bot_the_snapshot_lost_is_skipped()
    {
        var order = GroupPlan.LeadersFirst(new Bot?[] { null, Snapshot[0] }, bot => bot!.Leader);

        Assert.Single(order);
    }

    [Fact]
    public void A_leader_takes_the_members_still_alive_as_followers()
    {
        var sizes = GroupPlan.Sizes(Snapshot, bot => bot.Group);

        Assert.Equal(2, GroupPlan.FollowersOf(sizes, 7));
    }

    [Fact]
    public void A_leader_alone_has_no_follower()
    {
        var sizes = GroupPlan.Sizes(Snapshot, bot => bot.Group);

        Assert.Equal(0, GroupPlan.FollowersOf(sizes, 3));
        Assert.Equal(0, GroupPlan.FollowersOf(sizes, null));
        Assert.Equal(0, GroupPlan.FollowersOf(sizes, 99));
    }

    [Fact]
    public void Only_a_group_of_several_bots_is_shared()
    {
        var sizes = GroupPlan.Sizes(Snapshot, bot => bot.Group);

        Assert.True(GroupPlan.IsShared(sizes, 7));
        Assert.False(GroupPlan.IsShared(sizes, 3));
        Assert.False(GroupPlan.IsShared(sizes, null));
    }

    [Fact]
    public void Loaded_bots_come_back_in_order_without_waiting_for_the_others()
    {
        string[] loaded = ["boss", "guard 1", "lone scav"];
        var waiting = GroupPlan.LeadersFirst(Snapshot, bot => bot.Leader);

        var ready = GroupPlan.ReadyNow(waiting, bot => loaded.Contains(bot.Name), bot => bot.Leader, bot => bot.Group);

        Assert.Equal(["boss", "guard 1", "lone scav"], ready.Select(bot => bot.Name));
    }

    [Fact]
    public void A_guard_waits_for_its_leader_still_loading()
    {
        string[] loaded = ["guard 1", "guard 2", "lone scav", "old snapshot"];
        var waiting = GroupPlan.LeadersFirst(Snapshot, bot => bot.Leader);

        var ready = GroupPlan.ReadyNow(waiting, bot => loaded.Contains(bot.Name), bot => bot.Leader, bot => bot.Group);

        Assert.Equal(["lone scav", "old snapshot"], ready.Select(bot => bot.Name));
    }

    [Fact]
    public void Guards_come_back_once_their_leader_was_given_up_on()
    {
        // The leader that never loaded is taken out of the waiting list
        var waiting = Snapshot.Where(bot => bot.Name != "boss");

        var ready = GroupPlan.ReadyNow(waiting, _ => true, bot => bot.Leader, bot => bot.Group);

        Assert.Equal(["guard 1", "lone scav", "guard 2", "old snapshot"], ready.Select(bot => bot.Name));
    }

    [Fact]
    public void Bots_of_an_older_snapshot_have_no_group()
    {
        var sizes = GroupPlan.Sizes([new Bot("a"), new Bot("b")], bot => bot.Group);

        Assert.Empty(sizes);
    }
}
