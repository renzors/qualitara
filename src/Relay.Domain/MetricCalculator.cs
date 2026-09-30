namespace Relay.Domain;

/// <summary>The three robust metric methods of §5: counts, rates and durations.</summary>
public static class MetricCalculator
{
    /// <summary>Volume: median of baseline counts, spread max(1.4826·MAD, √median, 1).</summary>
    public static MetricResult Count(MetricDefinition def, int recent, IReadOnlyList<int> baseline, Thresholds t, EngineOptions o)
    {
        if (baseline.Count == 0) return Insufficient(def, recent, null, recent, "no comparison periods");

        var values = baseline.Select(b => (double)b).ToArray();
        double median = Stats.Median(values);
        double spread = Stats.CountSpread(values, median);
        double z = (recent - median) / spread;

        var status = StatusFor(z, t);
        if (recent == 0 && median >= o.ZeroVolumeMedian) status = MetricStatus.Unusual;

        return Build(def, status, recent, median, median - t.Look * spread, median + t.Look * spread, z, recent, null, null);
    }

    /// <summary>
    /// Rate: centre = pooled baseline rate p₀; spread = max(binomial SD at the recent sample size, 1.4826·MAD of the
    /// per-period rates). The binomial SD uses p₀ smoothed by half an event so a baseline of 0% or 100% still has noise.
    /// </summary>
    public static MetricResult Rate(MetricDefinition def, int num, int den, IReadOnlyList<(int Num, int Den)> baseline,
        Thresholds t, EngineOptions o, string? detail = null)
    {
        double? value = den > 0 ? (double)num / den : null;
        var (baseNum, baseDen) = CappedPool(baseline, o.RatePoolWeightCap);
        double? p0 = baseDen > 0 ? baseNum / baseDen : null;

        if (den < o.MinRateSample)
            return Insufficient(def, value, p0, den, $"{den} {def.SampleUnit}, needs {o.MinRateSample}", detail);
        if (baseDen < o.MinRateSample)
            return Insufficient(def, value, p0, den, $"only {baseDen:0} {def.SampleUnit} in the comparison periods", detail);

        double p = value!.Value, pc = p0!.Value;
        double pAdj = (baseNum + 0.5) / (baseDen + 1.0);
        double binomial = Math.Sqrt(pAdj * (1 - pAdj) / den);
        var periodRates = baseline.Where(b => b.Den > 0).Select(b => (double)b.Num / b.Den).ToArray();
        double mad = periodRates.Length >= 2 ? Stats.MadScale * Stats.Mad(periodRates, Stats.Median(periodRates)) : 0;
        double spread = Math.Max(binomial, mad);
        double z = (p - pc) / spread;

        var (wl, wh) = Stats.Wilson(num, den);
        return Build(def, StatusFor(z, t), p, pc, Math.Max(0, pc - t.Look * spread), Math.Min(1, pc + t.Look * spread), z, den, wl, wh, detail);
    }

    /// <summary>
    /// Duration: median of the recent calls vs the median of the baseline period medians, spread max(1.4826·MAD, floor).
    /// Only baseline periods with enough qualifying calls count, and enough such periods are required.
    /// </summary>
    public static MetricResult Duration(MetricDefinition def, IReadOnlyList<double> recent, IReadOnlyList<IReadOnlyList<double>> baseline,
        int minPeriods, Thresholds t, EngineOptions o)
    {
        double? value = recent.Count > 0 ? Stats.Median(recent) : null;
        var medians = baseline.Where(b => b.Count >= o.MinDurationCalls).Select(b => Stats.Median(b)).ToArray();
        double? centre = medians.Length > 0 ? Stats.Median(medians) : null;

        if (recent.Count < o.MinDurationCalls)
            return Insufficient(def, value, centre, recent.Count, $"{recent.Count} {def.SampleUnit}, needs {o.MinDurationCalls}");
        if (medians.Length < minPeriods)
            return Insufficient(def, value, centre, recent.Count, $"only {medians.Length} comparison periods with {o.MinDurationCalls}+ {def.SampleUnit}");

        double c = centre!.Value;
        double spread = Math.Max(Stats.MadScale * Stats.Mad(medians, c), o.DurationSpreadFloorSeconds);
        double z = (value!.Value - c) / spread;
        return Build(def, StatusFor(z, t), value, c, c - t.Look * spread, c + t.Look * spread, z, recent.Count, null, null);
    }

    /// <summary>
    /// Pools baseline numerators and denominators, but caps each period's weight at <paramref name="cap"/>× the median
    /// period size (keeping its rate), so one spike period cannot dominate the pooled rate (decision D6).
    /// </summary>
    public static (double Num, double Den) CappedPool(IReadOnlyList<(int Num, int Den)> baseline, double cap)
    {
        var used = baseline.Where(b => b.Den > 0).ToList();
        if (used.Count == 0) return (0, 0);
        double limit = Math.Max(cap * Stats.Median(used.Select(b => (double)b.Den)), 1);
        return (used.Sum(b => b.Num * Math.Min(1.0, limit / b.Den)), used.Sum(b => Math.Min(b.Den, limit)));
    }

    public static MetricStatus StatusFor(double z, Thresholds t)
    {
        double a = Math.Abs(z);
        return a >= t.Unusual ? MetricStatus.Unusual : a >= t.Look ? MetricStatus.WorthALook : MetricStatus.Normal;
    }

    public static Polarity PolarityFor(MetricDefinition def, Direction d) => d switch
    {
        Direction.Higher => def.HigherIs,
        Direction.Lower => def.HigherIs switch { Polarity.Good => Polarity.Bad, Polarity.Bad => Polarity.Good, _ => Polarity.Neutral },
        _ => Polarity.Neutral,
    };

    /// <summary>Marks a computed metric as not assessed because its scope lacks history. Values stay for display.</summary>
    public static MetricResult AsInsufficient(MetricResult m, string why) => m with
    {
        Status = MetricStatus.InsufficientData,
        Z = null,
        Direction = Direction.Same,
        Polarity = Polarity.Neutral,
        Display = Format.Display(m.Definition.Kind, m.Value, m.Expected, null, null, Direction.Same),
        Reason = $"{Label(m.Definition, m.Detail)}: not assessed ({why})",
    };

    private static MetricResult Build(MetricDefinition def, MetricStatus status, double? value, double centre, double low, double high,
        double z, int n, double? valueLow, double? valueHigh, string? detail = null)
    {
        var direction = z > 0 ? Direction.Higher : z < 0 ? Direction.Lower : Direction.Same;
        var polarity = PolarityFor(def, direction);
        bool flagged = status is MetricStatus.Unusual or MetricStatus.WorthALook;
        var display = Format.Display(def.Kind, value, centre, low, high, flagged ? direction : Direction.Same);

        string reason = flagged
            ? $"{Label(def, detail)} {display.Value} vs usual {display.Low}–{display.High} ({Word(direction, polarity)})"
            : $"{Label(def, detail)} {display.Value}, within usual {display.Low}–{display.High}";

        return new MetricResult(def, status, value, centre, low, high, z, direction, polarity, n, valueLow, valueHigh, display, reason, detail);
    }

    private static MetricResult Insufficient(MetricDefinition def, double? value, double? centre, int n, string why, string? detail = null) =>
        new(def, MetricStatus.InsufficientData, value, centre, null, null, null, Direction.Same, Polarity.Neutral, n, null, null,
            Format.Display(def.Kind, value, centre, null, null, Direction.Same),
            $"{Label(def, detail)}: not assessed ({why})", detail);

    private static string Label(MetricDefinition def, string? detail) =>
        string.IsNullOrEmpty(detail) ? def.Label : $"{def.Label} ({detail})";

    private static string Word(Direction d, Polarity p) => p switch
    {
        Polarity.Good => "better",
        Polarity.Bad => "worse",
        _ => d == Direction.Higher ? "higher" : "lower",
    };
}
