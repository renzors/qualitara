using Relay.Domain;

namespace Relay.Domain.Tests;

/// <summary>Builds deterministic synthetic accounts for engine tests.</summary>
internal sealed class Synthetic
{
    public static readonly DateTime DataStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime AsOf = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<ActivityEvent> _events = [];
    public string TimeZone { get; init; } = "UTC";
    public DateTime CreatedAt { get; init; } = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public IReadOnlyList<ActivityEvent> Events => _events;

    public Synthetic Add(DateTime at, EventType type = EventType.CallReceived, string? outcome = Outcomes.Connected,
        int? duration = 300, string location = "Site A")
    {
        _events.Add(new ActivityEvent(location, type, at, duration, outcome));
        return this;
    }

    /// <summary>
    /// Adds <paramref name="perDay"/> events every day in [from, to), spread over business hours (10:00–15:59 UTC),
    /// cycling through the given (type, outcome) mix.
    /// </summary>
    public Synthetic Daily(DateTime from, DateTime to, int perDay, (EventType Type, string? Outcome)[]? mix = null, string location = "Site A")
    {
        mix ??= [(EventType.CallReceived, Outcomes.Connected)];
        int n = 0;
        for (var day = from.Date; day < to; day = day.AddDays(1))
            for (int i = 0; i < perDay; i++, n++)
            {
                var (type, outcome) = mix[n % mix.Length];
                var at = DateTime.SpecifyKind(day.AddHours(10 + i % 6).AddMinutes(7 * (i / 6) % 60), DateTimeKind.Utc);
                Add(at, type, outcome, type == EventType.CallReceived ? 240 + (n % 5) * 30 : null, location);
            }
        return this;
    }

    public PreparedAccount Prepare() =>
        new(new AccountInfo(1, "Test Co", "Testing", TimeZone, CreatedAt), _events);

    public static AccountAssessment Assess(Synthetic s, int window = 7, Sensitivity sensitivity = Sensitivity.Normal, DateTime? asOf = null,
        EngineOptions? options = null) =>
        new AssessmentEngine(options).Assess(s.Prepare(), new AssessmentRequest(asOf ?? AsOf, window, sensitivity), DataStart);
}
