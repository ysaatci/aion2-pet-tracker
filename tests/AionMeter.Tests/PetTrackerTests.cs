using AionMeter.Core.Events;
using PetTracker.Core;

namespace AionMeter.Tests;

public class PetTrackerTests
{
    private const int Varg = 1018, Armadon = 1046;
    private const int VargNpc = 2400385, ArmadonNpc = 2400100, UnknownNpc = 2400999;

    private sealed class MemoryStorage : IPetStateStorage
    {
        public PetState? Saved;
        public PetState? Load() => Saved;
        public void Save(PetState state) => Saved = state;
    }

    private static PetCatalog Catalog()
    {
        var c = new PetCatalog();
        c.Add(new PetInfo(Varg, "Varg Berserker", Genus.Cogni, 5, [VargNpc]));
        c.Add(new PetInfo(Armadon, "Armadon", Genus.Fera, 5, [ArmadonNpc]));
        return c;
    }

    private static PetCollectionEvent Login(int vargLevel, int vargSouls) =>
        new(1_000, Armadon, new Dictionary<int, int> { [Varg] = vargLevel }, new Dictionary<int, int> { [Varg] = vargSouls });

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 25)]
    [InlineData(2, 75)]
    public void Thresholds_follow_the_global_levels(int level, int needed) => Assert.Equal(needed, SoulThresholds.Needed(level));

    [Fact]
    public void Max_level_needs_nothing() => Assert.Null(SoulThresholds.Needed(3));

    [Fact]
    public void Soul_drops_advance_the_login_snapshot()
    {
        var store = new PetProgressStore(Catalog());
        store.ApplySnapshot(1_000, Armadon, new Dictionary<int, int> { [Varg] = 2 }, new Dictionary<int, int> { [Varg] = 11 });
        for (var i = 0; i < 4; i++) store.ApplySoul(2_000 + i, Varg, 1);
        Assert.Equal(new PetProgress(Varg, 2, 15, 75), store.Get(Varg));
    }

    [Fact]
    public void Filling_the_bar_levels_up_and_restarts_the_count()
    {
        var store = new PetProgressStore(Catalog());
        store.ApplySnapshot(1_000, 0, new Dictionary<int, int>(), new Dictionary<int, int> { [Varg] = 4 });
        Assert.Equal(new PetProgress(Varg, 1, 0, 25), store.ApplySoul(2_000, Varg, 1));
    }

    [Fact]
    public void Max_level_pets_stay_at_zero()
    {
        var store = new PetProgressStore(Catalog());
        store.ApplySnapshot(1_000, 0, new Dictionary<int, int> { [Varg] = 2 }, new Dictionary<int, int> { [Varg] = 74 });
        Assert.Equal(new PetProgress(Varg, 3, 0, null), store.ApplySoul(2_000, Varg, 3));
    }

    [Fact]
    public void State_survives_a_restart()
    {
        var storage = new MemoryStorage();
        var first = new PetProgressStore(Catalog(), storage);
        first.ApplySnapshot(1_000, Armadon, new Dictionary<int, int> { [Varg] = 2 }, new Dictionary<int, int> { [Varg] = 11 });
        first.ApplySoul(2_000, Varg, 1);
        var second = new PetProgressStore(Catalog(), storage);
        Assert.True(second.HasSnapshot);
        Assert.Equal(12, second.Get(Varg).Souls);
    }

    [Fact]
    public void Nearby_soul_sources_are_grouped_by_genus_with_progress()
    {
        var tracker = new PetTrackerService(Catalog(), new PetProgressStore(Catalog()));
        tracker.Process(Login(2, 11));
        tracker.Process(new NpcSeenEvent(2_000, 1, VargNpc));
        tracker.Process(new NpcSeenEvent(2_000, 2, VargNpc));
        tracker.Process(new NpcSeenEvent(2_000, 3, ArmadonNpc));
        tracker.Process(new NpcSeenEvent(2_000, 4, UnknownNpc));
        var snap = tracker.Snapshot();
        Assert.True(snap.HasCollection);
        var cogni = Assert.Single(snap.NearbyByGenus[Genus.Cogni]);
        Assert.Equal(("Varg Berserker", 2, 11, 2), (cogni.Pet.Name, cogni.Progress.Level, cogni.Progress.Souls, cogni.NearbyCount));
        Assert.Equal(0, Assert.Single(snap.NearbyByGenus[Genus.Fera]).Progress.Level);
    }

    [Fact]
    public void Dead_and_departed_npcs_leave_the_list()
    {
        var tracker = new PetTrackerService(Catalog(), new PetProgressStore(Catalog()));
        tracker.Process(new NpcSeenEvent(1_000, 1, VargNpc));
        tracker.Process(new NpcSeenEvent(1_000, 2, ArmadonNpc));
        tracker.Process(new DeathEvent(2_000, 1));
        Assert.False(tracker.Snapshot().NearbyByGenus.ContainsKey(Genus.Cogni));
        tracker.Process(new NpcHpEvent(1_000 + NearbyNpcTracker.StaleMs + 10, 99, 1, 1));
        Assert.Empty(tracker.Snapshot().NearbyByGenus);
    }

    [Fact]
    public void A_soul_after_a_kill_teaches_an_unknown_source()
    {
        var catalog = Catalog();
        var tracker = new PetTrackerService(catalog, new PetProgressStore(catalog));
        tracker.Process(new NpcSeenEvent(1_000, 7, UnknownNpc));
        tracker.Process(new DeathEvent(5_000, 7));
        tracker.Process(new PetSoulGainedEvent(5_350, Varg, 1));
        Assert.Contains(Varg, catalog.PetsDroppedBy(UnknownNpc));
        Assert.Equal((UnknownNpc, Varg), Assert.Single(tracker.Learner.Learned));
    }

    [Fact]
    public void Ambiguous_kills_teach_nothing()
    {
        var catalog = Catalog();
        var tracker = new PetTrackerService(catalog, new PetProgressStore(catalog));
        tracker.Process(new NpcSeenEvent(1_000, 7, UnknownNpc));
        tracker.Process(new NpcSeenEvent(1_000, 8, 2400998));
        tracker.Process(new DeathEvent(5_000, 7));
        tracker.Process(new DeathEvent(5_100, 8));
        tracker.Process(new PetSoulGainedEvent(5_350, Armadon, 1));
        Assert.Empty(tracker.Learner.Learned);
    }
}
