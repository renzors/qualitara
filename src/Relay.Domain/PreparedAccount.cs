namespace Relay.Domain;

/// <summary>An event after dedupe, with its account-local timestamp precomputed.</summary>
public sealed record LocalEvent(ActivityEvent Raw, DateTime Local)
{
    public DateTime Utc => Raw.OccurredAtUtc;
}

/// <summary>
/// An account's events cleaned once and sorted by time: exact duplicates removed (§5.5) and every
/// timestamp converted to the account's IANA timezone (DST-correct). Raw data is never mutated.
/// </summary>
public sealed class PreparedAccount
{
    private readonly LocalEvent[] _events;
    private readonly DateTime[] _times;

    public AccountInfo Account { get; }
    public TimeZoneInfo TimeZone { get; }
    public int DuplicatesRemoved { get; }
    public IReadOnlyList<LocalEvent> Events => _events;
    public IReadOnlyList<string> Locations { get; }

    public PreparedAccount(AccountInfo account, IEnumerable<ActivityEvent> rawEvents)
    {
        Account = account;
        TimeZone = TimeZones.Find(account.TimeZoneId);

        var raw = rawEvents.ToList();
        var unique = new HashSet<ActivityEvent>(raw);
        DuplicatesRemoved = raw.Count - unique.Count;

        _events = unique
            .Select(e => new LocalEvent(e, TimeZones.ToLocal(e.OccurredAtUtc, TimeZone)))
            .OrderBy(e => e.Utc)
            .ToArray();
        _times = _events.Select(e => e.Utc).ToArray();
        Locations = _events.Select(e => e.Raw.Location).Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    public bool HasAnyEvents => _events.Length > 0;

    /// <summary>Events in (start, end], found by binary search.</summary>
    public ReadOnlySpan<LocalEvent> In(Period p)
    {
        int from = UpperBound(p.StartUtc), to = UpperBound(p.EndUtc);
        return to > from ? _events.AsSpan(from, to - from) : ReadOnlySpan<LocalEvent>.Empty;
    }

    /// <summary>First index whose time is strictly greater than t.</summary>
    private int UpperBound(DateTime t)
    {
        int lo = 0, hi = _times.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_times[mid] <= t) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}

public static class TimeZones
{
    public static TimeZoneInfo Find(string ianaId)
    {
        if (string.Equals(ianaId, "UTC", StringComparison.OrdinalIgnoreCase)) return TimeZoneInfo.Utc;
        return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
}
