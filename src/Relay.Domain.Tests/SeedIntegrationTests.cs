using Microsoft.EntityFrameworkCore;
using Relay.Data;
using Relay.Domain;

namespace Relay.Domain.Tests;

/// <summary>Migrates a fresh SQLite file (schema + seed) once and checks the acceptance scenarios of SPEC §10.</summary>
public sealed class SeedDatabase : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"relay-test-{Guid.NewGuid():N}.db");
    public RelayDbContext Db { get; private set; } = null!;
    public DatasetBounds Bounds { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Db = new RelayDbContext(new DbContextOptionsBuilder<RelayDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);
        await Db.Database.MigrateAsync();
        Bounds = (await new ActivityStore(Db).BoundsAsync())!;
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        File.Delete(_path);
    }

    public async Task<PreparedAccount> AccountAsync(int id)
    {
        var store = new ActivityStore(Db);
        return new PreparedAccount((await store.AccountAsync(id))!, await store.EventsAsync(id));
    }
}

public class SeedIntegrationTests(SeedDatabase seed) : IClassFixture<SeedDatabase>
{
    private readonly AssessmentEngine _engine = new();

    [Fact]
    public async Task Migrations_load_the_whole_seed()
    {
        Assert.Equal(20, await seed.Db.Accounts.CountAsync());
        Assert.Equal(12_626, await seed.Db.ActivityEvents.CountAsync());
        Assert.Equal(new DateTime(2026, 7, 27, 22, 20, 34, DateTimeKind.Utc), seed.Bounds.LastEventUtc);
    }

    [Fact]
    public async Task Default_view_gives_every_account_a_state_and_account_20_has_no_activity()
    {
        var req = new AssessmentRequest(seed.Bounds.LastEventUtc);
        for (int id = 1; id <= 20; id++)
        {
            var a = _engine.Assess(await seed.AccountAsync(id), req, seed.Bounds.StartUtc);
            if (id == 20) Assert.Equal(Verdict.NoActivity, a.Overall.Verdict);
            else Assert.NotEqual(Verdict.NoActivity, a.Overall.Verdict);
        }
    }

    [Fact]
    public async Task Account_6_on_june_3_is_unusual_with_volume_up_across_locations()
    {
        var asOf = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);
        var a = _engine.Assess(await seed.AccountAsync(6), new AssessmentRequest(asOf, 1), seed.Bounds.StartUtc);

        Assert.Equal(Verdict.Unusual, a.Overall.Verdict);
        var total = a.Overall.Metric("total")!;
        Assert.Equal(Direction.Higher, total.Direction);
        Assert.True(total.Value > 800);
        Assert.Equal(15, a.Locations.Count);
        Assert.All(a.Locations, l => Assert.Equal(Verdict.Unusual, l.Assessment.Verdict));
    }

    [Fact]
    public async Task Account_6_default_view_is_not_blown_up_by_the_spike_in_its_baseline()
    {
        var a = _engine.Assess(await seed.AccountAsync(6), new AssessmentRequest(seed.Bounds.LastEventUtc), seed.Bounds.StartUtc);
        var total = a.Overall.Metric("total")!;
        Assert.Equal(MetricStatus.Normal, total.Status);
        Assert.InRange(total.ExpectedHigh!.Value, 0, 150);
        Assert.NotNull(a.Quality.BaselineNote);
    }

    [Fact]
    public async Task Duplicates_removed_sum_to_twelve()
    {
        int sum = 0;
        for (int id = 1; id <= 20; id++) sum += (await seed.AccountAsync(id)).DuplicatesRemoved;
        Assert.Equal(12, sum);
    }

    [Fact]
    public async Task Strict_to_relaxed_never_raises_flag_counts()
    {
        for (int id = 1; id <= 19; id++)
        {
            var p = await seed.AccountAsync(id);
            int Flags(Sensitivity s) => _engine.Assess(p, new AssessmentRequest(seed.Bounds.LastEventUtc, 7, s), seed.Bounds.StartUtc)
                .Overall.Metrics.Count(m => m.IsFlagged);
            Assert.True(Flags(Sensitivity.Strict) >= Flags(Sensitivity.Normal), $"account {id}");
            Assert.True(Flags(Sensitivity.Normal) >= Flags(Sensitivity.Relaxed), $"account {id}");
        }
    }
}
