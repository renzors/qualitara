using Relay.Domain;

namespace Relay.Domain.Tests;

public class PeriodPlanTests
{
    private static readonly DateTime AsOf = new(2026, 7, 27, 22, 20, 34, DateTimeKind.Utc);

    [Fact]
    public void Weekly_window_uses_the_eight_prior_weeks()
    {
        var plan = PeriodPlan.Build(AsOf, 7, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), EngineOptions.Default);
        Assert.Equal(new Period(AsOf.AddDays(-7), AsOf), plan.Recent);
        Assert.Equal(8, plan.Baseline.Count);
        Assert.Equal(new Period(AsOf.AddDays(-14), AsOf.AddDays(-7)), plan.Baseline[0]);
        Assert.Equal(new Period(AsOf.AddDays(-63), AsOf.AddDays(-56)), plan.Baseline[7]);
    }

    [Fact]
    public void Daily_window_compares_the_same_weekday()
    {
        var plan = PeriodPlan.Build(AsOf, 1, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), EngineOptions.Default);
        Assert.Equal(8, plan.Baseline.Count);
        Assert.All(plan.Baseline, p =>
        {
            Assert.Equal(TimeSpan.FromDays(1), p.EndUtc - p.StartUtc);
            Assert.Equal(AsOf.DayOfWeek, p.EndUtc.DayOfWeek);
            Assert.Equal(AsOf.TimeOfDay, p.EndUtc.TimeOfDay);
        });
    }

    [Fact]
    public void Monthly_window_uses_four_periods_and_needs_three()
    {
        var plan = PeriodPlan.Build(AsOf, 30, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), EngineOptions.Default);
        Assert.Equal(4, plan.Baseline.Count);
        Assert.Equal(3, plan.MinBaselineRequired);
    }

    [Fact]
    public void Periods_before_the_earliest_usable_instant_are_dropped_not_zero_filled()
    {
        var created = AsOf.AddDays(-7 * 4 - 3); // room for 3 full prior weeks only
        var plan = PeriodPlan.Build(AsOf, 7, created, EngineOptions.Default);
        Assert.Equal(3, plan.Baseline.Count);
        Assert.False(plan.HasEnoughPeriods);
        Assert.All(plan.Baseline, p => Assert.True(p.StartUtc >= created));
    }

    [Fact]
    public void Periods_are_start_exclusive_and_end_inclusive()
    {
        var p = new Period(AsOf.AddDays(-1), AsOf);
        Assert.False(p.Contains(AsOf.AddDays(-1)));
        Assert.True(p.Contains(AsOf));
    }

    [Fact]
    public void Unsupported_window_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PeriodPlan.Build(AsOf, 14, AsOf.AddDays(-200), EngineOptions.Default));
}

public class PreparedAccountTests
{
    [Fact]
    public void Exact_duplicates_are_counted_once_and_reported()
    {
        var at = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var s = new Synthetic()
            .Add(at).Add(at)                              // identical: duplicate
            .Add(at, outcome: Outcomes.Missed)            // differs by outcome: kept
            .Add(at, duration: null)                      // differs by duration: kept
            .Add(at, location: "Site B");                 // differs by location: kept
        var p = s.Prepare();
        Assert.Equal(1, p.DuplicatesRemoved);
        Assert.Equal(4, p.Events.Count);
    }

    [Theory]
    // US DST starts 2026-03-08 at 2am local: 06:30Z is 1:30 CST before, 07:30Z is 3:30 CDT after.
    [InlineData("America/Chicago", "2026-03-08T07:30:00Z", 1)]
    [InlineData("America/Chicago", "2026-03-08T08:30:00Z", 3)]
    // US DST ends 2026-11-01 at 2am local: 06:30Z is 1:30 CDT, 07:30Z is 1:30 CST.
    [InlineData("America/Chicago", "2026-11-01T06:30:00Z", 1)]
    [InlineData("America/Chicago", "2026-11-01T07:30:00Z", 1)]
    [InlineData("America/New_York", "2026-07-01T03:00:00Z", 23)]
    // Phoenix never observes DST: always UTC−7.
    [InlineData("America/Phoenix", "2026-01-15T15:00:00Z", 8)]
    [InlineData("America/Phoenix", "2026-07-15T15:00:00Z", 8)]
    [InlineData("UTC", "2026-07-15T15:00:00Z", 15)]
    public void Local_hours_follow_the_account_timezone(string tz, string utc, int expectedHour)
    {
        var at = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var p = new Synthetic { TimeZone = tz }.Add(DateTime.SpecifyKind(at, DateTimeKind.Utc)).Prepare();
        Assert.Equal(expectedHour, p.Events[0].Local.Hour);
    }
}

public class FormatTests
{
    [Fact]
    public void Count_ranges_round_inward_so_a_flagged_value_sits_outside()
    {
        var d = Format.Display(MetricKind.Count, 12, 6, 0.6, 11.8, Direction.Higher);
        Assert.Equal(("12", "1", "11"), (d.Value, d.Low, d.High));
    }

    [Fact]
    public void Flagged_rates_round_away_from_the_range()
    {
        var d = Format.Display(MetricKind.Rate, 0.116, 0.2, 0.118, 0.28, Direction.Lower);
        Assert.Equal(("11%", "12%", "28%"), (d.Value, d.Low, d.High));
    }

    [Fact]
    public void Ranges_never_show_negative_zero()
    {
        var d = Format.Display(MetricKind.Rate, 0.05, 0.02, -0.03, 0.07, Direction.Same);
        Assert.Equal("0%", d.Low);
    }

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(252, "4m 12s")]
    public void Durations_read_as_minutes_and_seconds(double s, string expected) => Assert.Equal(expected, Format.Duration(s));

    [Fact]
    public void Hour_ranges_wrap_midnight()
    {
        var h = new bool[24];
        foreach (var i in new[] { 20, 21, 22, 23, 0, 1, 2, 11 }) h[i] = true;
        Assert.Equal("11am–12pm, 8pm–3am", Format.HourRanges(h));
    }
}
