using Microsoft.EntityFrameworkCore;
using Relay.Domain;

namespace Relay.Data;

/// <summary>The dataset's time span. Baselines never reach before <see cref="StartUtc"/>.</summary>
public sealed record DatasetBounds(DateTime FirstEventUtc, DateTime LastEventUtc)
{
    /// <summary>Midnight UTC of the first event's day: the earliest instant the data covers.</summary>
    public DateTime StartUtc => DateTime.SpecifyKind(FirstEventUtc.Date, DateTimeKind.Utc);

    /// <summary>End of the last event's UTC day, the latest as-of a user can pick.</summary>
    public DateTime LastDayEndUtc => DateTime.SpecifyKind(LastEventUtc.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
}

/// <summary>Read-only access to accounts and events, mapped to engine types. Rows are never modified.</summary>
public sealed class ActivityStore(RelayDbContext db)
{
    public async Task<DatasetBounds?> BoundsAsync(CancellationToken ct = default)
    {
        var times = db.ActivityEvents.AsNoTracking().Select(e => e.OccurredAt);
        if (!await times.AnyAsync(ct)) return null;
        var first = await times.OrderBy(t => t).FirstAsync(ct);
        var last = await times.OrderByDescending(t => t).FirstAsync(ct);
        return new DatasetBounds(first, last);
    }

    public async Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct = default) =>
        (await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct)).Select(ToDomain).ToList();

    public async Task<AccountInfo?> AccountAsync(int id, CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking().Where(a => a.Id == id).Select(a => a).FirstOrDefaultAsync(ct) is { } a ? ToDomain(a) : null;

    public async Task<IReadOnlyList<ActivityEvent>> EventsAsync(int accountId, CancellationToken ct = default) =>
        (await db.ActivityEvents.AsNoTracking().Where(e => e.AccountId == accountId).ToListAsync(ct))
            .Select(ToDomain).OfType<ActivityEvent>().ToList();

    public async Task<ILookup<int, ActivityEvent>> AllEventsAsync(CancellationToken ct = default) =>
        (await db.ActivityEvents.AsNoTracking().ToListAsync(ct))
            .Select(e => (e.AccountId, Event: ToDomain(e)))
            .Where(x => x.Event is not null)
            .ToLookup(x => x.AccountId, x => x.Event!);

    private static AccountInfo ToDomain(Entities.Account a) =>
        new(a.Id, a.Name, a.Industry, a.Timezone, DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc));

    /// <summary>Unknown event types are skipped rather than guessed at.</summary>
    private static ActivityEvent? ToDomain(Entities.ActivityEvent e) =>
        EventTypes.Parse(e.EventType) is { } type
            ? new ActivityEvent(e.Location, type, DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc), e.DurationSeconds, e.Outcome)
            : null;
}
