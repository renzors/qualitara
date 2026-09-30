using Relay.Domain;

namespace Relay.Domain.Tests;

public class StatsTests
{
    [Theory]
    [InlineData(new double[] { 5 }, 5)]
    [InlineData(new double[] { 3, 1, 2 }, 2)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 2.5)]
    public void Median_handles_odd_and_even_counts(double[] values, double expected) =>
        Assert.Equal(expected, Stats.Median(values));

    [Fact]
    public void Mad_is_median_absolute_deviation()
    {
        double[] v = [1, 1, 2, 2, 4, 6, 9];
        Assert.Equal(1, Stats.Mad(v, Stats.Median(v)));
    }

    [Fact]
    public void Count_spread_uses_poisson_floor_when_mad_is_zero()
    {
        double[] flat = [16, 16, 16, 16, 16, 16, 16, 16];
        Assert.Equal(4, Stats.CountSpread(flat, 16));
    }

    [Fact]
    public void Count_spread_never_drops_below_one()
    {
        double[] zeros = [0, 0, 0, 0];
        Assert.Equal(1, Stats.CountSpread(zeros, 0));
    }

    [Fact]
    public void Count_spread_uses_scaled_mad_when_larger()
    {
        double[] v = [10, 20, 30, 40, 50];
        Assert.Equal(1.4826 * 10, Stats.CountSpread(v, 30), 6);
    }

    [Fact]
    public void Wilson_interval_contains_the_observed_rate_and_stays_in_bounds()
    {
        var (lo, hi) = Stats.Wilson(0, 10);
        Assert.Equal(0, lo);
        Assert.InRange(hi, 0.25, 0.35);

        var (lo2, hi2) = Stats.Wilson(41, 100);
        Assert.InRange(0.41, lo2, hi2);
    }
}
