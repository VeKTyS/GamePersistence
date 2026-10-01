using RaidRecovery.Server.Storage;

namespace RaidRecovery.Server.Tests;

public sealed class SnapshotStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SnapshotStore _store;

    public SnapshotStoreTests()
    {
        _store = new SnapshotStore(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    private string Current => Path.Combine(_temp.Path, Samples.ProfileId + ".json");

    private string Previous => Path.Combine(_temp.Path, Samples.ProfileId + ".prev.json");

    [Fact]
    public void Read_returns_null_when_nothing_was_written()
    {
        Assert.Null(_store.Read(Samples.ProfileId));
    }

    [Fact]
    public void Write_then_read_returns_the_snapshot()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot(map: "factory4_day"));

        var read = _store.Read(Samples.ProfileId);

        Assert.NotNull(read);
        Assert.Equal("factory4_day", read.Map);
        Assert.Equal(1380, read.Raid?.SecondsLeft);
    }

    [Fact]
    public void Write_keeps_the_squad_of_a_teammate_mod()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot() with { Squad = ["1234567", "7654321"] });

        var read = _store.Read(Samples.ProfileId);

        Assert.Equal(["1234567", "7654321"], read!.Squad);
    }

    [Fact]
    public void A_snapshot_written_before_the_squad_existed_reads_without_one()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot());

        Assert.Null(_store.Read(Samples.ProfileId)!.Squad);
    }

    [Fact]
    public void Write_keeps_opaque_blocks_untouched()
    {
        var snapshot = Samples.Snapshot() with
        {
            Player = new Models.PlayerState
            {
                Position = new Models.Position { X = 112.4f, Y = 2.1f, Z = -308.7f },
                Rotation = new Models.Rotation { Yaw = 214.5f, Pitch = -3.2f },
                Profile = Samples.Json("""{"Health":{"Hydration":{"Current":78}},"Inventory":{"items":[{"_id":"a"}]}}"""),
            },
        };

        _store.Write(Samples.ProfileId, snapshot);
        var read = _store.Read(Samples.ProfileId);

        Assert.Equal(-308.7f, read!.Player!.Position!.Z);
        Assert.Equal(214.5f, read.Player.Rotation!.Yaw);
        var profile = read.Player.Profile!.Value;
        Assert.Equal(78, profile.GetProperty("Health").GetProperty("Hydration").GetProperty("Current").GetInt32());
        Assert.Equal("a", profile.GetProperty("Inventory").GetProperty("items")[0].GetProperty("_id").GetString());
    }

    [Fact]
    public void Second_write_rotates_the_first_into_previous()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot(sessionId: "first"));
        _store.Write(Samples.ProfileId, Samples.Snapshot(sessionId: "second"));

        Assert.Contains("second", File.ReadAllText(Current));
        Assert.Contains("first", File.ReadAllText(Previous));
    }

    [Fact]
    public void Write_leaves_no_temporary_file()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot());
        _store.Write(Samples.ProfileId, Samples.Snapshot());

        Assert.Empty(Directory.GetFiles(_temp.Path, "*.tmp"));
    }

    [Fact]
    public void Read_falls_back_to_previous_when_current_is_truncated()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot(sessionId: "first"));
        _store.Write(Samples.ProfileId, Samples.Snapshot(sessionId: "second"));
        // Simulates a crash in the middle of a write: the current file stops halfway through the JSON
        File.WriteAllText(Current, """{"version":1,"sessionId":"sec""");

        var read = _store.Read(Samples.ProfileId);

        Assert.Equal("first", read?.SessionId);
    }

    [Fact]
    public void Read_returns_null_when_both_files_are_corrupt()
    {
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(Current, "{");
        File.WriteAllText(Previous, "not json");

        Assert.Null(_store.Read(Samples.ProfileId));
    }

    [Fact]
    public void Delete_removes_both_files_and_reports_it()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot());
        _store.Write(Samples.ProfileId, Samples.Snapshot());

        Assert.True(_store.Delete(Samples.ProfileId));

        Assert.Empty(Directory.GetFiles(_temp.Path));
        Assert.False(_store.Delete(Samples.ProfileId));
    }

    [Fact]
    public void Profiles_do_not_see_each_other()
    {
        _store.Write(Samples.ProfileId, Samples.Snapshot());

        Assert.Null(_store.Read(Samples.OtherProfileId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\..\\profiles\\6ab58e74984af4a498879c6d")]
    [InlineData("../evil")]
    [InlineData("6ab58e74984af4a498879c6")]
    [InlineData("6ab58e74984af4a498879c6dZ")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Anything_that_is_not_a_profile_id_is_rejected(string profileId)
    {
        Assert.Throws<ArgumentException>(() => _store.Write(profileId, Samples.Snapshot()));
        Assert.Throws<ArgumentException>(() => _store.Read(profileId));
        Assert.Throws<ArgumentException>(() => _store.Delete(profileId));
    }
}
