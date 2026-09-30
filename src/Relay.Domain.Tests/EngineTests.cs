using Relay.Domain;

namespace Relay.Domain.Tests;

public class EngineTests
{
    private static readonly (EventType, string?)[] Mix =
    [
        (EventType.CallReceived, Outcomes.Connected), (EventType.CallReceived, Outcomes.Connected), (EventType.CallReceived, Outcomes.Missed),
        (EventType.CallReceived, Outcomes.Voicemail), (EventType.LeadCreated, Outcomes.Open), (EventType.LeadCreated, Outcomes.Converted),
        (EventType.AppointmentSet, Outcomes.Completed), (EventType.AppointmentSet, Outcomes.NoShow),
    ];

    /// <summary>10 steady weeks at 8 events/day, then a recent week built by the caller.</summary>
    private static Synthetic Steady(int perDay = 8) =>
        new Synthetic().Daily(Synthetic.DataStart, Synthetic.AsOf.AddDays(-7), perDay, Mix);

    [Fact]
    public void Steady_activity_is_normal()
    {
        var s = Steady().Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 8, Mix);
        var a = Synthetic.Assess(s);
        Assert.Equal(Verdict.Normal, a.Overall.Verdict);
        Assert.Empty(a.Overall.Reasons);
        Assert.All(a.Overall.Metrics.Where(m => m.Status != MetricStatus.InsufficientData), m => Assert.Equal(MetricStatus.Normal, m.Status));
    }

    [Fact]
    public void A_collapse_in_volume_is_unusual_with_a_reason()
    {
        var s = Steady().Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 1, Mix);
        var a = Synthetic.Assess(s);
        Assert.Equal(Verdict.Unusual, a.Overall.Verdict);
        Assert.StartsWith("All activity 7 vs usual", a.Overall.Reasons[0]);
        Assert.True(a.Overall.Reasons.Count <= 3);
    }

    [Fact]
    public void No_events_ever_means_no_activity()
    {
        var a = Synthetic.Assess(new Synthetic());
        Assert.Equal(Verdict.NoActivity, a.Overall.Verdict);
        Assert.Empty(a.Locations);
    }

    [Fact]
    public void Too_few_baseline_periods_means_insufficient_history_with_values_still_shown()
    {
        var s = new Synthetic { CreatedAt = Synthetic.AsOf.AddDays(-7 * 3 - 1) }
            .Daily(Synthetic.AsOf.AddDays(-21), Synthetic.AsOf, 8, Mix);
        var a = Synthetic.Assess(s);
        Assert.Equal(Verdict.InsufficientHistory, a.Overall.Verdict);
        Assert.Contains("only 2 of 8", a.Overall.Note);
        var total = a.Overall.Metric("total")!;
        Assert.Equal(56, total.Value);
        Assert.Equal(MetricStatus.InsufficientData, total.Status);
        Assert.Null(total.Z);
    }

    [Fact]
    public void Low_volume_scope_is_insufficient_history()
    {
        var s = new Synthetic();
        for (var d = Synthetic.DataStart; d < Synthetic.AsOf; d = d.AddDays(7)) s.Add(d.AddHours(12)).Add(d.AddHours(13));
        var a = Synthetic.Assess(s);
        Assert.Equal(Verdict.InsufficientHistory, a.Overall.Verdict);
        Assert.Contains("fewer than 3 events", a.Overall.Note);
    }

    [Fact]
    public void Low_volume_scope_still_flags_a_large_surge()
    {
        var s = new Synthetic();
        for (var d = Synthetic.DataStart; d < Synthetic.AsOf.AddDays(-7); d = d.AddDays(7)) s.Add(d.AddHours(12));
        s.Daily(Synthetic.AsOf.AddDays(-1), Synthetic.AsOf, 40);
        var a = Synthetic.Assess(s);
        Assert.Equal(Verdict.Unusual, a.Overall.Verdict);
        Assert.Single(a.Overall.Reasons);
        Assert.StartsWith("All activity 40", a.Overall.Reasons[0]);
    }

    [Fact]
    public void Null_outcomes_are_excluded_from_rates_and_their_share_is_flagged()
    {
        var recent = Enumerable.Repeat((EventType.CallReceived, (string?)null), 3)
            .Concat([(EventType.CallReceived, Outcomes.Connected), (EventType.CallReceived, Outcomes.Missed)]).ToArray();
        var s = Steady(10).Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 10, recent);
        var a = Synthetic.Assess(s);

        var missed = a.Overall.Metric("missed_rate")!;
        Assert.Equal(28, missed.SampleSize);         // only the 28 calls with a known outcome
        Assert.Equal(0.5, missed.Value!.Value, 6);   // 14 missed of 28 known

        var noOutcome = a.Overall.Metric("no_outcome")!;
        Assert.Equal(MetricStatus.Unusual, noOutcome.Status);
        Assert.Equal(Polarity.Bad, noOutcome.Polarity);
        Assert.Equal(42, a.Quality.RecentNoOutcome);
    }

    [Fact]
    public void Null_durations_are_excluded_from_talk_time()
    {
        var s = Steady();
        for (int i = 0; i < 20; i++) s.Add(Synthetic.AsOf.AddHours(-30 - i), duration: i < 12 ? 270 : null);
        var talk = Synthetic.Assess(s).Overall.Metric("talk_time")!;
        Assert.Equal(12, talk.SampleSize);
        Assert.Equal(270, talk.Value);
    }

    [Fact]
    public void Night_activity_is_flagged_and_names_the_local_hours()
    {
        var s = Steady(12);
        for (int i = 0; i < 30; i++) s.Add(Synthetic.AsOf.AddDays(-1 - i % 6).AddHours(3));    // 3am UTC, never seen before
        s.Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 12, Mix);
        var odd = Synthetic.Assess(s).Overall.Metric("odd_hours")!;
        Assert.Equal(MetricStatus.Unusual, odd.Status);
        Assert.Equal("3am–4am", odd.Detail);
        Assert.Equal(Polarity.Neutral, odd.Polarity);
    }

    [Fact]
    public void Daily_window_uses_weekday_matched_baseline()
    {
        // Busy Mondays (40), quiet other days (5). A normal Monday must not be flagged against quiet days.
        var s = new Synthetic();
        for (var d = Synthetic.DataStart; d < Synthetic.AsOf; d = d.AddDays(1))
            s.Daily(d, d.AddDays(1), d.DayOfWeek == DayOfWeek.Monday ? 40 : 5);
        var monday = new DateTime(2026, 3, 30, 23, 59, 59, DateTimeKind.Utc);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);

        var a = Synthetic.Assess(s, window: 1, asOf: monday);
        Assert.Equal(40, a.Overall.Metric("total")!.Value);
        Assert.Equal(40, a.Overall.Metric("total")!.Expected);
        Assert.Equal(MetricStatus.Normal, a.Overall.Metric("total")!.Status);
    }

    [Fact]
    public void Relaxing_sensitivity_never_adds_flags()
    {
        var s = Steady().Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 5, Mix);
        int Flags(Sensitivity x) => Synthetic.Assess(s, sensitivity: x).Overall.Metrics.Count(m => m.IsFlagged);
        Assert.True(Flags(Sensitivity.Strict) >= Flags(Sensitivity.Normal));
        Assert.True(Flags(Sensitivity.Normal) >= Flags(Sensitivity.Relaxed));
    }

    [Fact]
    public void Locations_are_assessed_separately_and_new_ones_are_noted()
    {
        var s = Steady()
            .Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 8, Mix)
            .Daily(Synthetic.DataStart, Synthetic.AsOf.AddDays(-7), 6, location: "Site B")        // Site B goes quiet
            .Daily(Synthetic.AsOf.AddDays(-3), Synthetic.AsOf, 4, location: "Site C");            // Site C is new
        var a = Synthetic.Assess(s);

        var b = a.Locations.Single(l => l.Location == "Site B");
        Assert.Equal(Verdict.Unusual, b.Assessment.Verdict);
        Assert.Equal(0, b.Assessment.RecentTotal);

        var c = a.Locations.Single(l => l.Location == "Site C");
        Assert.True(c.IsNew);
        Assert.Equal(Verdict.InsufficientHistory, c.Assessment.Verdict);
        Assert.Contains("New location", c.Assessment.Note);

        Assert.Equal(Verdict.Normal, a.Locations.Single(l => l.Location == "Site A").Assessment.Verdict);
        Assert.Equal("Site B", a.Locations[0].Location); // most urgent first
    }

    [Fact]
    public void A_spike_inside_the_baseline_is_explained_and_does_not_mask_the_current_week()
    {
        var s = Steady().Add(Synthetic.AsOf.AddDays(-20)); // placeholder so the week exists
        for (int i = 0; i < 800; i++) s.Add(Synthetic.AsOf.AddDays(-20).AddSeconds(i + 1));
        s.Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 8, Mix);

        var a = Synthetic.Assess(s);
        Assert.True(a.Overall.Verdict == Verdict.Normal, string.Join("; ", a.Overall.Reasons));
        Assert.Contains("unusually busy", a.Quality.BaselineNote);
    }

    [Fact]
    public void Weekly_series_marks_the_spike_week_and_its_peak_day()
    {
        var s = Steady();
        for (int i = 0; i < 500; i++) s.Add(Synthetic.AsOf.AddDays(-17).AddSeconds(i + 1));
        s.Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 8, Mix);

        var weeks = new AssessmentEngine().WeeklySeries(s.Prepare(), new AssessmentRequest(Synthetic.AsOf), Synthetic.DataStart, Synthetic.AsOf);
        var spike = weeks.Single(w => w.Period.Contains(Synthetic.AsOf.AddDays(-17)));
        Assert.Equal(Verdict.Unusual, spike.Verdict);
        Assert.True(spike.PeakDay!.Count >= 500);
        Assert.Equal(Synthetic.AsOf, weeks[^1].Period.EndUtc);
        Assert.Equal(Verdict.InsufficientHistory, weeks[0].Verdict);
    }

    [Fact]
    public void Verdict_history_is_oldest_first_and_null_before_the_data()
    {
        var s = Steady().Daily(Synthetic.AsOf.AddDays(-7), Synthetic.AsOf, 8, Mix);
        var h = new AssessmentEngine().VerdictHistory(s.Prepare(), new AssessmentRequest(Synthetic.AsOf), Synthetic.DataStart, 25);
        Assert.Equal(25, h.Count);
        Assert.Null(h[0]);
        Assert.Equal(Verdict.Normal, h[^1]);
    }
}
