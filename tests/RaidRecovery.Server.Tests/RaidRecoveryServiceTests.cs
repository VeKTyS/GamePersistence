using RaidRecovery.Server.Services;
using RaidRecovery.Server.Storage;

namespace RaidRecovery.Server.Tests;

public sealed class RaidRecoveryServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 13, 12, 11, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new(Start);
    private readonly SnapshotStore _store;
    private readonly RaidRecoveryService _service;

    public RaidRecoveryServiceTests()
    {
        _store = new SnapshotStore(_temp.Path);
        _service = new RaidRecoveryService(_store, _clock, TimeSpan.FromHours(24));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Save_stamps_profile_and_date_from_the_server_not_from_the_client()
    {
        var forged = Samples.Snapshot() with { ProfileId = Samples.OtherProfileId, SavedAt = Start.AddYears(10) };

        var outcome = _service.Save(Samples.ProfileId, forged);

        Assert.Equal(SaveOutcome.Saved, outcome);
        var stored = _store.Read(Samples.ProfileId);
        Assert.Equal(Samples.ProfileId, stored?.ProfileId);
        Assert.Equal(Start, stored?.SavedAt);
        Assert.Null(_store.Read(Samples.OtherProfileId));
    }

    [Fact]
    public void Save_works_when_the_server_never_saw_the_raid_start()
    {
        // Server restarted in the middle of a raid: the state is unknown, the capture must not be lost
        Assert.Equal(SaveOutcome.Saved, _service.Save(Samples.ProfileId, Samples.Snapshot()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void Save_rejects_versions_it_cannot_read(int version)
    {
        Assert.Equal(SaveOutcome.UnsupportedVersion, _service.Save(Samples.ProfileId, Samples.Snapshot(version: version)));
        Assert.Null(_store.Read(Samples.ProfileId));
    }

    [Fact]
    public void Save_rejects_incomplete_snapshots()
    {
        Assert.Equal(SaveOutcome.InvalidSnapshot, _service.Save(Samples.ProfileId, null));
        Assert.Equal(SaveOutcome.InvalidSnapshot, _service.Save(Samples.ProfileId, Samples.Snapshot(sessionId: " ")));
        Assert.Equal(SaveOutcome.InvalidSnapshot, _service.Save(Samples.ProfileId, Samples.Snapshot(map: "")));
    }

    [Fact]
    public void Save_rejects_an_invalid_profile()
    {
        Assert.Equal(SaveOutcome.InvalidProfile, _service.Save("../evil", Samples.Snapshot()));
    }

    [Fact]
    public void Raid_end_purges_the_snapshot()
    {
        _service.OnRaidStarted(Samples.ProfileId);
        _service.Save(Samples.ProfileId, Samples.Snapshot());

        Assert.True(_service.OnRaidEnded(Samples.ProfileId));

        Assert.Null(_service.GetPending(Samples.ProfileId));
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void A_save_arriving_after_raid_end_is_refused()
    {
        _service.OnRaidStarted(Samples.ProfileId);
        _service.Save(Samples.ProfileId, Samples.Snapshot());
        _service.OnRaidEnded(Samples.ProfileId);

        // The send had already left when the raid ended
        var outcome = _service.Save(Samples.ProfileId, Samples.Snapshot());

        Assert.Equal(SaveOutcome.RaidClosed, outcome);
        Assert.Null(_service.GetPending(Samples.ProfileId));
    }

    [Fact]
    public void A_save_arriving_after_death_discard_is_refused()
    {
        _service.OnRaidStarted(Samples.ProfileId);
        _service.Save(Samples.ProfileId, Samples.Snapshot());
        _service.Discard(Samples.ProfileId);

        Assert.Equal(SaveOutcome.RaidClosed, _service.Save(Samples.ProfileId, Samples.Snapshot()));
        Assert.Null(_service.GetPending(Samples.ProfileId));
    }

    [Fact]
    public void Saves_are_accepted_again_once_a_new_raid_starts()
    {
        _service.OnRaidStarted(Samples.ProfileId);
        _service.OnRaidEnded(Samples.ProfileId);

        _service.OnRaidStarted(Samples.ProfileId);

        Assert.Equal(SaveOutcome.Saved, _service.Save(Samples.ProfileId, Samples.Snapshot()));
    }

    [Fact]
    public void Closing_one_profile_does_not_close_another()
    {
        _service.OnRaidEnded(Samples.ProfileId);

        Assert.Equal(SaveOutcome.Saved, _service.Save(Samples.OtherProfileId, Samples.Snapshot()));
    }

    [Fact]
    public void Raid_start_purges_a_leftover_snapshot()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot(map: "old_raid"));

        Assert.True(_service.OnRaidStarted(Samples.ProfileId));

        Assert.Null(_service.GetPending(Samples.ProfileId));
    }

    [Fact]
    public void Pending_returns_the_snapshot_left_by_a_crash()
    {
        _service.OnRaidStarted(Samples.ProfileId);
        _service.Save(Samples.ProfileId, Samples.Snapshot(map: "woods"));
        // No raid end: the client crashed

        var pending = _service.GetPending(Samples.ProfileId);

        Assert.Equal("woods", pending?.Map);
    }

    [Fact]
    public void Pending_does_not_consume_the_snapshot()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());

        _service.GetPending(Samples.ProfileId);

        Assert.NotNull(_service.GetPending(Samples.ProfileId));
    }

    [Fact]
    public void Snapshot_is_still_offered_just_before_the_age_limit()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());

        _clock.Advance(TimeSpan.FromHours(24));

        Assert.NotNull(_service.GetPending(Samples.ProfileId));
    }

    [Fact]
    public void Snapshot_older_than_the_age_limit_is_ignored_and_deleted()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());

        _clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        Assert.Null(_service.GetPending(Samples.ProfileId));
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    private static readonly Func<Models.Snapshot, Task<string?>> ApplySucceeds = _ => Task.FromResult<string?>(null);

    [Fact]
    public async Task Expired_snapshot_cannot_be_restored()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());
        _clock.Advance(TimeSpan.FromHours(25));
        var applied = false;

        var result = await _service.RestoreAsync(Samples.ProfileId, _ => { applied = true; return Task.FromResult<string?>(null); });

        Assert.False(result.Restored);
        Assert.Equal("NoSnapshot", result.Failure);
        Assert.False(applied);
    }

    [Fact]
    public async Task Restore_applies_the_snapshot_it_returns()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot(map: "shoreline"));
        string? appliedMap = null;

        var result = await _service.RestoreAsync(Samples.ProfileId, s => { appliedMap = s.Map; return Task.FromResult<string?>(null); });

        Assert.True(result.Restored);
        Assert.Equal("shoreline", result.Snapshot?.Map);
        Assert.Equal("shoreline", appliedMap);
    }

    [Fact]
    public async Task Restore_can_only_be_played_once()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot(map: "shoreline"));
        var applications = 0;
        Task<string?> Apply(Models.Snapshot _) { applications++; return Task.FromResult<string?>(null); }

        var first = await _service.RestoreAsync(Samples.ProfileId, Apply);
        var second = await _service.RestoreAsync(Samples.ProfileId, Apply);

        Assert.True(first.Restored);
        Assert.False(second.Restored);
        Assert.Equal(1, applications);
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public async Task Restore_consumes_the_previous_file_too()
    {
        // Two writes: there is a current file and a previous one. If only the current one were deleted,
        // a second recovery would fall back on the previous one and duplicate the loot.
        _service.Save(Samples.ProfileId, Samples.Snapshot(sessionId: "a"));
        _service.Save(Samples.ProfileId, Samples.Snapshot(sessionId: "a"));

        await _service.RestoreAsync(Samples.ProfileId, ApplySucceeds);

        Assert.False((await _service.RestoreAsync(Samples.ProfileId, ApplySucceeds)).Restored);
    }

    [Fact]
    public async Task Failed_application_keeps_the_snapshot_for_another_try()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot(map: "woods"));

        var failed = await _service.RestoreAsync(Samples.ProfileId, _ => Task.FromResult<string?>("ApplyFailed"));

        Assert.False(failed.Restored);
        Assert.Equal("ApplyFailed", failed.Failure);
        Assert.Equal("woods", _service.GetPending(Samples.ProfileId)?.Map);
        Assert.True((await _service.RestoreAsync(Samples.ProfileId, ApplySucceeds)).Restored);
    }

    [Fact]
    public async Task Concurrent_restores_apply_the_snapshot_once()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());
        var applications = 0;
        async Task<string?> SlowApply(Models.Snapshot _)
        {
            Interlocked.Increment(ref applications);
            await Task.Delay(50);
            return null;
        }

        var results = await Task.WhenAll(
            _service.RestoreAsync(Samples.ProfileId, SlowApply),
            _service.RestoreAsync(Samples.ProfileId, SlowApply)
        );

        Assert.Equal(1, applications);
        Assert.Single(results, r => r.Restored);
    }

    [Fact]
    public void Discard_deletes_the_snapshot()
    {
        _service.Save(Samples.ProfileId, Samples.Snapshot());

        Assert.True(_service.Discard(Samples.ProfileId));

        Assert.Null(_service.GetPending(Samples.ProfileId));
        Assert.False(_service.Discard(Samples.ProfileId));
    }

    [Fact]
    public void Unreadable_files_are_cleaned_up_instead_of_lingering()
    {
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(Path.Combine(_temp.Path, Samples.ProfileId + ".json"), "{");

        Assert.Null(_service.GetPending(Samples.ProfileId));

        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public async Task Lifecycle_calls_ignore_an_invalid_profile()
    {
        Assert.False(_service.OnRaidStarted(""));
        Assert.False(_service.OnRaidEnded("../evil"));
        Assert.False(_service.Discard("../evil"));
        Assert.Null(_service.GetPending("../evil"));
        Assert.False((await _service.RestoreAsync("../evil", ApplySucceeds)).Restored);
    }
}
