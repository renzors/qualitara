namespace Relay.Domain;

public readonly record struct Thresholds(double Look, double Unusual);

/// <summary>Every tunable constant of the engine in one place (decision D3).</summary>
public sealed class EngineOptions
{
    public static EngineOptions Default { get; } = new();

    /// <summary>A local hour is "rare" when it holds less than this share of baseline events.</summary>
    public double RareHourShare { get; init; } = 0.02;

    /// <summary>Minimum known-outcome events in the recent period (and in the pooled baseline) for a rate metric.</summary>
    public int MinRateSample { get; init; } = 10;

    /// <summary>Minimum usable baseline periods for W = 1 and W = 7.</summary>
    public int MinBaselinePeriods { get; init; } = 4;

    /// <summary>Minimum usable baseline periods for W = 30 (only ~6 months of data exist).</summary>
    public int MinBaselinePeriodsMonthly { get; init; } = 3;

    /// <summary>Below this median events per baseline period, a scope has "insufficient history".</summary>
    public double MinBaselineMedianVolume { get; init; } = 3;

    /// <summary>Minimum connected calls with a duration, in the recent period and in each counted baseline period.</summary>
    public int MinDurationCalls { get; init; } = 10;

    public double DurationSpreadFloorSeconds { get; init; } = 30;

    /// <summary>Recent volume of zero against a baseline median at or above this is always "unusual".</summary>
    public double ZeroVolumeMedian { get; init; } = 3;

    /// <summary>
    /// A low-volume scope (enough periods, but usually fewer than <see cref="MinBaselineMedianVolume"/> events) is not assessed,
    /// except that an "unusual" surge in total volume of at least this many events is still flagged (decision D5).
    /// </summary>
    public int LowVolumeSurgeMinEvents { get; init; } = 10;

    /// <summary>In a pooled baseline rate, no period weighs more than this multiple of the median period size (decision D6).</summary>
    public double RatePoolWeightCap { get; init; } = 3;

    public int BaselinePeriodsFor(int windowDays) => windowDays == 30 ? 4 : 8;

    public int MinBaselinePeriodsFor(int windowDays) => windowDays == 30 ? MinBaselinePeriodsMonthly : MinBaselinePeriods;

    public Thresholds ThresholdsFor(Sensitivity s) => s switch
    {
        Sensitivity.Relaxed => new(2.5, 4.0),
        Sensitivity.Strict => new(1.5, 2.5),
        _ => new(2.0, 3.0),
    };
}
