using RaidRecovery.Server.Models;
using RaidRecovery.Server.Services;
using RaidRecovery.Server.Storage;

namespace RaidRecovery.Server.Tests;

/// <summary>What the players of a raid are told about the one who hosted it.</summary>
public sealed class LastResumeTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 29, 18, 0, 0, TimeSpan.Zero));
    private readonly RaidRecoveryService _service;

    public LastResumeTests()
    {
        _service = new RaidRecoveryService(new SnapshotStore(_directory.Path), _clock, TimeSpan.FromHours(24));
    }

    public void Dispose() => _directory.Dispose();

    private static Task<string?> ApplySucceeds(Snapshot snapshot) => Task.FromResult<string?>(null);

    [Fact]
    public void A_host_who_resumed_nothing_is_not_back()
    {
        Assert.Null(_service.LastResume(Samples.ProfileId));
    }

    [Fact]
    public async Task A_resume_is_dated_by_the_clock_of_the_server()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());
        _clock.Advance(TimeSpan.FromMinutes(3));

        await _service.RestoreAsync(Samples.ProfileId, ApplySucceeds);

        Assert.Equal(new DateTimeOffset(2026, 9, 29, 18, 3, 0, TimeSpan.Zero), _service.LastResume(Samples.ProfileId));
    }

    [Fact]
    public async Task A_resume_that_failed_does_not_count()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());

        await _service.RestoreAsync(Samples.ProfileId, _ => Task.FromResult<string?>("ApplyFailed"));

        Assert.Null(_service.LastResume(Samples.ProfileId));
    }

    [Fact]
    public async Task The_resume_of_one_player_says_nothing_of_another()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());
        await _service.RestoreAsync(Samples.ProfileId, ApplySucceeds);

        Assert.Null(_service.LastResume(Samples.OtherProfileId));
    }

    [Fact]
    public async Task The_date_outlives_the_raid_so_a_late_player_still_learns_the_host_came_back()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());
        await _service.RestoreAsync(Samples.ProfileId, ApplySucceeds);

        _service.OnRaidStarted(Samples.ProfileId);
        _service.OnRaidEnded(Samples.ProfileId);

        Assert.NotNull(_service.LastResume(Samples.ProfileId));
    }

    [Fact]
    public void A_made_up_identifier_is_answered_like_an_unknown_player()
    {
        Assert.Null(_service.LastResume("../evil"));
        Assert.Null(_service.LastResume(null));
    }

    [Fact]
    public void The_mark_of_the_coop_mod_is_kept_as_it_was_sent()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot() with { Coop = "client:aaaaaaaaaaaaaaaaaaaaaaaa" });

        Assert.Equal("client:aaaaaaaaaaaaaaaaaaaaaaaa", _service.GetPending(Samples.ProfileId)?.Coop);
    }
}
