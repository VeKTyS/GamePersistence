using RaidRecovery.Server.Services;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace RaidRecovery.Server.Tests;

public class LootReplayServiceTests
{
    private const string Map = "factory4_day";

    private readonly MemoryLootStore _store = new();
    private readonly LootReplayService _service;

    public LootReplayServiceTests()
    {
        _service = new LootReplayService(_store);
    }

    /// <summary>Loot different from the sample, to tell "replayed" from "newly generated".</summary>
    private static List<SpawnpointTemplate> OtherLoot()
    {
        return
        [
            new SpawnpointTemplate
            {
                Id = "other",
                IsContainer = false,
                Root = "f00000000000000000000009",
                Items = [Loot.Item("f00000000000000000000009")],
            },
        ];
    }

    [Fact]
    public void A_normal_raid_keeps_its_loot_and_stores_it()
    {
        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());

        Assert.False(decision.Replayed);
        Assert.Equal(Loot.Ids(Loot.Sample()), Loot.Ids(_store.ReadLoot(Samples.ProfileId)!.Loot));
    }

    [Fact]
    public void A_resumed_raid_gets_the_loot_of_the_interrupted_raid_minus_what_was_taken()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage, Loot.Wrench]));

        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.True(decision.Replayed);
        Assert.Equal(2, decision.Removed);
        Assert.Equal([Loot.Crate, Loot.Bolts, Loot.Rifle, Loot.Round, Loot.Magazine], Loot.Ids(decision.Replacement!));
    }

    [Fact]
    public void Without_a_recovery_the_next_raid_on_the_same_map_gets_new_loot()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());

        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.False(decision.Replayed);
        Assert.Equal(Loot.Ids(OtherLoot()), Loot.Ids(_store.ReadLoot(Samples.ProfileId)!.Loot));
    }

    [Fact]
    public void A_recovery_is_served_once()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));
        _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.False(decision.Replayed);
    }

    [Fact]
    public void A_raid_launched_on_another_map_gets_new_loot()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));

        var decision = _service.OnLootGenerated(Samples.ProfileId, "bigmap", OtherLoot());

        Assert.False(decision.Replayed);
        Assert.Equal("bigmap", _store.ReadLoot(Samples.ProfileId)!.Map);
    }

    [Fact]
    public void Taken_items_add_up_over_several_recoveries()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));
        _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        // Second crash: the bandage was used up in the meantime, it is no longer in the inventory
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Wrench]));
        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.True(decision.Replayed);
        Assert.DoesNotContain(Loot.Bandage, Loot.Ids(decision.Replacement!));
        Assert.DoesNotContain(Loot.Wrench, Loot.Ids(decision.Replacement!));
    }

    [Fact]
    public void A_resumed_raid_does_not_overwrite_the_stored_loot()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));

        _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.Equal(1, _store.LootWrites);
        Assert.Equal(Loot.Ids(Loot.Sample()), Loot.Ids(_store.ReadLoot(Samples.ProfileId)!.Loot));
    }

    [Fact]
    public void A_new_raid_forgets_the_items_taken_in_the_previous_one()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));
        _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());

        Assert.Empty(_store.ReadNotes(Samples.ProfileId).Taken);
    }

    [Fact]
    public void Forgetting_a_raid_removes_its_loot_and_cancels_the_recovery()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));

        _service.Forget(Samples.ProfileId);
        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.False(decision.Replayed);
    }

    [Fact]
    public void A_recovery_without_stored_loot_falls_back_to_new_loot()
    {
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));

        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());

        Assert.False(decision.Replayed);
        Assert.NotNull(_store.ReadLoot(Samples.ProfileId));
    }

    /// <summary>Stand-in for SPT's serializer: a body is "corpse:&lt;id&gt;", anything else is unreadable.</summary>
    private static SpawnpointTemplate? ParseCorpse(string json)
    {
        if (!json.StartsWith("corpse:"))
        {
            throw new FormatException("Not a body");
        }

        var id = json["corpse:".Length..];
        return new SpawnpointTemplate { Id = "body-" + id, Root = id, Items = [Loot.Item(id)] };
    }

    [Fact]
    public void Bodies_of_the_snapshot_are_added_to_the_replayed_loot()
    {
        var service = new LootReplayService(_store, ParseCorpse);
        service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        service.Arm(Samples.ProfileId, Map, new RecoveryTicket([], ["corpse:b00000000000000000000001", "corpse:b00000000000000000000002"]));

        var decision = service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.Equal(2, decision.Corpses);
        Assert.Equal(5, decision.Replacement!.Count);
        Assert.Contains(decision.Replacement, spawnpoint => spawnpoint.Id == "body-b00000000000000000000001");
    }

    [Fact]
    public void A_body_that_cannot_be_read_is_skipped_without_losing_the_loot()
    {
        var service = new LootReplayService(_store, ParseCorpse);
        service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        service.Arm(Samples.ProfileId, Map, new RecoveryTicket([], ["garbage", "corpse:b00000000000000000000001"]));

        var decision = service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.True(decision.Replayed);
        Assert.Equal(1, decision.Corpses);
        Assert.Equal(4, decision.Replacement!.Count);
    }

    [Fact]
    public void Bodies_are_those_of_the_last_snapshot_only()
    {
        var service = new LootReplayService(_store, ParseCorpse);
        service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        service.Arm(Samples.ProfileId, Map, new RecoveryTicket([], ["corpse:b00000000000000000000001"]));
        service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        // The second snapshot already lists every body of the map, the first one included
        service.Arm(Samples.ProfileId, Map, new RecoveryTicket([], ["corpse:b00000000000000000000002"]));
        var decision = service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.Equal(1, decision.Corpses);
        Assert.DoesNotContain(decision.Replacement!, spawnpoint => spawnpoint.Id == "body-b00000000000000000000001");
    }

    [Fact]
    public void Bodies_do_not_end_up_in_the_stored_loot()
    {
        var service = new LootReplayService(_store, ParseCorpse);
        service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        service.Arm(Samples.ProfileId, Map, new RecoveryTicket([], ["corpse:b00000000000000000000001"]));

        service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.Equal(3, _store.ReadLoot(Samples.ProfileId)!.Loot.Count);
    }

    [Fact]
    public void The_time_left_travels_with_the_recovery_when_the_snapshot_holds_the_bots()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([], ["corpse:b00000000000000000000001"], 1380, BotsAlive: 4));

        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        Assert.Equal(1380, decision.SecondsLeft);
        // Four alive and one body: five bots the map must not spawn again
        Assert.Equal(5, decision.BotsInSnapshot);
    }

    [Fact]
    public void Each_recovery_of_a_raid_is_counted()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        Assert.Equal(0, _service.ResumesDone(Samples.ProfileId));

        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([]));
        _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([]));

        Assert.Equal(2, _service.ResumesDone(Samples.ProfileId));
    }

    [Fact]
    public void A_new_raid_starts_the_count_of_recoveries_again()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([]));
        _service.OnLootGenerated(Samples.ProfileId, Map, OtherLoot());

        // No recovery before this start: it is a new raid
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());

        Assert.Equal(0, _service.ResumesDone(Samples.ProfileId));
    }

    [Fact]
    public void A_new_raid_carries_no_time_left()
    {
        var decision = _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());

        Assert.Null(decision.SecondsLeft);
    }

    [Fact]
    public void Profiles_do_not_share_their_loot()
    {
        _service.OnLootGenerated(Samples.ProfileId, Map, Loot.Sample());
        _service.Arm(Samples.ProfileId, Map, new RecoveryTicket([Loot.Bandage]));

        var decision = _service.OnLootGenerated(Samples.OtherProfileId, Map, OtherLoot());

        Assert.False(decision.Replayed);
    }

    [Fact]
    public void An_invalid_profile_identifier_writes_nothing()
    {
        var decision = _service.OnLootGenerated("../../evil", Map, Loot.Sample());

        Assert.False(decision.Replayed);
        Assert.Equal(0, _store.LootWrites);
    }
}
