using Relay.Domain;

namespace Relay.Domain.Tests;

public class MetricCalculatorTests
{
    private static readonly EngineOptions O = EngineOptions.Default;
    private static readonly Thresholds Normal = O.ThresholdsFor(Sensitivity.Normal);

    [Fact]
    public void Count_within_poisson_noise_is_normal_even_when_mad_is_zero()
    {
        var m = MetricCalculator.Count(Metrics.Total, 22, [16, 16, 16, 16, 16, 16, 16, 16], Normal, O);
        Assert.Equal(1.5, m.Z!.Value, 6); // (22 − 16) / √16
        Assert.Equal(MetricStatus.Normal, m.Status);
    }

    [Fact]
    public void Count_far_above_usual_is_unusual_and_good()
    {
        var m = MetricCalculator.Count(Metrics.Calls, 60, [20, 22, 19, 21, 20, 23, 18, 20], Normal, O);
        Assert.Equal(MetricStatus.Unusual, m.Status);
        Assert.Equal(Direction.Higher, m.Direction);
        Assert.Equal(Polarity.Good, m.Polarity);
        Assert.Contains("(better)", m.Reason);
    }

    [Fact]
    public void Zero_volume_against_a_real_baseline_is_always_unusual()
    {
        // Relaxed thresholds would put z = −3/√3 ≈ −1.7 below "worth a look"; the zero rule still flags it.
        var m = MetricCalculator.Count(Metrics.Total, 0, [3, 3, 3, 3, 3, 3, 3, 3], O.ThresholdsFor(Sensitivity.Relaxed), O);
        Assert.Equal(MetricStatus.Unusual, m.Status);
        Assert.Equal(Polarity.Bad, m.Polarity);
    }

    [Fact]
    public void Zero_volume_against_a_tiny_baseline_is_not_forced()
    {
        var m = MetricCalculator.Count(Metrics.Leads, 0, [1, 2, 1, 1, 2, 1, 1, 2], Normal, O);
        Assert.Equal(MetricStatus.Normal, m.Status);
    }

    [Fact]
    public void A_single_huge_spike_in_the_baseline_does_not_widen_the_band()
    {
        int[] clean = [60, 64, 58, 70, 66, 61, 63, 67];
        int[] spiked = [60, 64, 58, 881, 66, 61, 63, 67];
        var a = MetricCalculator.Count(Metrics.Total, 100, clean, Normal, O);
        var b = MetricCalculator.Count(Metrics.Total, 100, spiked, Normal, O);

        Assert.InRange(b.ExpectedHigh!.Value - b.ExpectedLow!.Value, 0, (a.ExpectedHigh!.Value - a.ExpectedLow!.Value) * 1.3);
        Assert.Equal(MetricStatus.Unusual, b.Status);
    }

    [Fact]
    public void Rate_with_small_sample_is_not_assessed()
    {
        var m = MetricCalculator.Rate(Metrics.MissedRate, 5, 9, Enumerable.Repeat((2, 10), 8).ToList(), Normal, O);
        Assert.Equal(MetricStatus.InsufficientData, m.Status);
        Assert.Null(m.Z);
        Assert.Contains("needs 10", m.Reason);
    }

    [Fact]
    public void Rate_shift_on_a_small_sample_does_not_flag()
    {
        // 4 of 12 missed (33%) against a steady 20%: the binomial spread at n = 12 absorbs it.
        var m = MetricCalculator.Rate(Metrics.MissedRate, 4, 12, Enumerable.Repeat((20, 100), 8).ToList(), Normal, O);
        Assert.Equal(MetricStatus.Normal, m.Status);
    }

    [Fact]
    public void Rate_shift_on_a_large_sample_flags()
    {
        // 41 of 100 missed against a steady 20%.
        var m = MetricCalculator.Rate(Metrics.MissedRate, 41, 100, Enumerable.Repeat((20, 100), 8).ToList(), Normal, O);
        Assert.Equal(MetricStatus.Unusual, m.Status);
        Assert.Equal(Polarity.Bad, m.Polarity);
        Assert.StartsWith("Missed-call rate 41% vs usual", m.Reason);
        Assert.InRange(0.41, m.ValueLow!.Value, m.ValueHigh!.Value);
    }

    [Fact]
    public void A_spike_period_cannot_dominate_the_pooled_rate()
    {
        // Seven weeks at 25% missed, one spike week of 800 calls with none missed.
        var baseline = Enumerable.Repeat((10, 40), 7).Append((0, 800)).ToList();
        var (num, den) = MetricCalculator.CappedPool(baseline, 3);
        Assert.Equal(70.0 / 400, num / den, 6); // spike week weighs 3 × 40 = 120 calls, at its own 0% rate

        var m = MetricCalculator.Rate(Metrics.MissedRate, 10, 40, baseline, Normal, O);
        Assert.Equal(MetricStatus.Normal, m.Status);
    }

    [Fact]
    public void Rate_spread_includes_real_week_to_week_variability()
    {
        // Baseline rates swing between 10% and 40%; a 40% week is ordinary here.
        var swinging = new List<(int, int)> { (10, 100), (40, 100), (15, 100), (35, 100), (10, 100), (40, 100), (20, 100), (30, 100) };
        var m = MetricCalculator.Rate(Metrics.MissedRate, 40, 100, swinging, Normal, O);
        Assert.Equal(MetricStatus.Normal, m.Status);
    }

    [Fact]
    public void Lower_conversion_is_bad_and_lower_missed_rate_is_good()
    {
        var baseline = Enumerable.Repeat((30, 100), 8).ToList();
        Assert.Equal(Polarity.Bad, MetricCalculator.Rate(Metrics.Conversion, 5, 100, baseline, Normal, O).Polarity);
        Assert.Equal(Polarity.Good, MetricCalculator.Rate(Metrics.MissedRate, 5, 100, baseline, Normal, O).Polarity);
    }

    [Fact]
    public void Duration_needs_enough_calls_and_periods()
    {
        var calls = Enumerable.Repeat(300.0, 12).ToList();
        IReadOnlyList<double> thin = Enumerable.Repeat(300.0, 3).ToList();
        IReadOnlyList<double> full = Enumerable.Repeat(300.0, 12).ToList();

        Assert.Equal(MetricStatus.InsufficientData,
            MetricCalculator.Duration(Metrics.TalkTime, calls.Take(5).ToList(), [full, full, full, full], 4, Normal, O).Status);
        Assert.Equal(MetricStatus.InsufficientData,
            MetricCalculator.Duration(Metrics.TalkTime, calls, [full, full, full, thin, thin], 4, Normal, O).Status);
        Assert.Equal(MetricStatus.Normal,
            MetricCalculator.Duration(Metrics.TalkTime, calls, [full, full, full, full], 4, Normal, O).Status);
    }

    [Fact]
    public void Duration_spread_has_a_30_second_floor_and_neutral_polarity()
    {
        IReadOnlyList<double> week = Enumerable.Repeat(300.0, 12).ToList();
        var longer = Enumerable.Repeat(400.0, 12).ToList();
        var m = MetricCalculator.Duration(Metrics.TalkTime, longer, [week, week, week, week, week], 4, Normal, O);
        Assert.Equal(100 / 30.0, m.Z!.Value, 6);
        Assert.Equal(MetricStatus.Unusual, m.Status);
        Assert.Equal(Polarity.Neutral, m.Polarity);
        Assert.Contains("(higher)", m.Reason);
    }

    [Theory]
    [InlineData(Sensitivity.Relaxed, 2.4, MetricStatus.Normal)]
    [InlineData(Sensitivity.Relaxed, 2.5, MetricStatus.WorthALook)]
    [InlineData(Sensitivity.Relaxed, 4.0, MetricStatus.Unusual)]
    [InlineData(Sensitivity.Normal, 2.0, MetricStatus.WorthALook)]
    [InlineData(Sensitivity.Normal, -3.0, MetricStatus.Unusual)]
    [InlineData(Sensitivity.Strict, 1.5, MetricStatus.WorthALook)]
    [InlineData(Sensitivity.Strict, 2.5, MetricStatus.Unusual)]
    public void Sensitivity_thresholds(Sensitivity s, double z, MetricStatus expected) =>
        Assert.Equal(expected, MetricCalculator.StatusFor(z, O.ThresholdsFor(s)));
}
