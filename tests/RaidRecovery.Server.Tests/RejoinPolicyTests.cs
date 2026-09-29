using RaidRecovery.Fika.Client;

namespace RaidRecovery.Server.Tests;

/// <summary>The rules of the Fika add-on that do not need the game. Decided with the player on 2026-09-29.</summary>
public class RejoinPolicyTests
{
    private const string Host = "6ab58e74984af4a498879c6d";

    private static readonly DateTimeOffset SavedAt = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_mark_of_a_client_names_its_host()
    {
        Assert.Equal(Host, RejoinPolicy.HostOf(RejoinPolicy.MarkOfClient(Host)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("host")]
    [InlineData("client:")]
    [InlineData("something else")]
    public void A_player_who_hosted_or_played_alone_has_no_host(string? mark)
    {
        Assert.Null(RejoinPolicy.HostOf(mark));
    }

    [Fact]
    public void The_host_resumed_when_the_server_dated_it_after_the_snapshot()
    {
        Assert.True(RejoinPolicy.HostResumedSince(SavedAt.AddMinutes(2), SavedAt));
        Assert.False(RejoinPolicy.HostResumedSince(SavedAt.AddMinutes(-40), SavedAt));
        Assert.False(RejoinPolicy.HostResumedSince(null, SavedAt));
    }

    [Theory]
    [InlineData(false, 1, true, false, HostRaid.None)]
    [InlineData(true, 0, false, false, HostRaid.Loading)]
    [InlineData(true, 2, false, false, HostRaid.Waiting)]
    [InlineData(true, 1, true, false, HostRaid.RunningWithPlayer)]
    [InlineData(true, 1, true, true, HostRaid.RunningPlayerDead)]
    [InlineData(true, 1, false, false, HostRaid.RunningWithoutPlayer)]
    [InlineData(true, 9, true, false, HostRaid.None)]
    public void The_list_of_fika_is_read(bool listed, int status, bool inRaid, bool dead, HostRaid expected)
    {
        Assert.Equal(expected, RejoinPolicy.Read(listed, status, inRaid, dead));
    }

    [Theory]
    // The host crashed and is not back: whatever Fika still lists is the raid that was cut
    [InlineData(false, HostRaid.None, Rejoin.Wait)]
    [InlineData(false, HostRaid.RunningWithoutPlayer, Rejoin.Wait)]
    [InlineData(false, HostRaid.Waiting, Rejoin.Wait)]
    // The host is back and waits for its players
    [InlineData(true, HostRaid.Waiting, Rejoin.JoinRestored)]
    [InlineData(true, HostRaid.Loading, Rejoin.Wait)]
    // The host started without waiting: the rule of Fika, not ours
    [InlineData(true, HostRaid.RunningWithoutPlayer, Rejoin.Gone)]
    [InlineData(true, HostRaid.RunningPlayerDead, Rejoin.Gone)]
    [InlineData(true, HostRaid.None, Rejoin.Gone)]
    // The player crashed, the host never stopped: Fika brings them back
    [InlineData(false, HostRaid.RunningWithPlayer, Rejoin.Reconnect)]
    [InlineData(true, HostRaid.RunningWithPlayer, Rejoin.Reconnect)]
    public void Going_back_to_the_raid(bool hostResumed, HostRaid raid, Rejoin expected)
    {
        Assert.Equal(expected, RejoinPolicy.Decide(hostResumed, raid));
    }

    [Theory]
    // Host not back, or gave up: the character of before the raid
    [InlineData(false, HostRaid.None, false)]
    [InlineData(false, HostRaid.RunningWithoutPlayer, false)]
    // Host resumed, in the lobby or in the raid: the player keeps what they carried
    [InlineData(true, HostRaid.Waiting, true)]
    [InlineData(true, HostRaid.RunningWithoutPlayer, true)]
    [InlineData(true, HostRaid.None, true)]
    // The raid never stopped for the host
    [InlineData(false, HostRaid.RunningWithPlayer, true)]
    [InlineData(false, HostRaid.RunningPlayerDead, true)]
    public void Leaving_the_raid(bool hostResumed, HostRaid raid, bool keepsGear)
    {
        Assert.Equal(keepsGear, RejoinPolicy.LeaveKeepsGear(hostResumed, raid));
    }

    [Fact]
    public void Clicking_leave_just_before_and_just_after_the_host_comes_back_does_not_give_the_same()
    {
        var before = RejoinPolicy.LeaveKeepsGear(RejoinPolicy.HostResumedSince(null, SavedAt), HostRaid.RunningWithoutPlayer);
        var after = RejoinPolicy.LeaveKeepsGear(RejoinPolicy.HostResumedSince(SavedAt.AddSeconds(1), SavedAt), HostRaid.Loading);

        Assert.False(before);
        Assert.True(after);
    }

    [Fact]
    public void A_refusal_always_tells_the_player_why()
    {
        foreach (var raid in Enum.GetValues<HostRaid>())
        {
            Assert.False(string.IsNullOrWhiteSpace(RejoinPolicy.Explain(Rejoin.Wait, raid)));
            Assert.False(string.IsNullOrWhiteSpace(RejoinPolicy.Explain(Rejoin.Gone, raid)));
        }
    }
}
