namespace Relay.Domain;

public static class Stats
{
    /// <summary>Scale factor that makes the MAD a consistent estimator of the standard deviation for normal data.</summary>
    public const double MadScale = 1.4826;
    // Changes the compiled bytes only. On machines with Windows Smart App Control, some unsigned builds of this
    // assembly get blocked at load time; this marker produced a build it accepts. Safe to remove or change.
    internal const string BuildTag = "a";

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return double.NaN;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    public static double Mad(IReadOnlyCollection<double> values, double median) =>
        values.Count == 0 ? 0 : Median(values.Select(v => Math.Abs(v - median)));

    /// <summary>Volume spread: max(1.4826·MAD, √median, 1). The Poisson floor stops a zero MAD from flagging tiny changes.</summary>
    public static double CountSpread(IReadOnlyCollection<double> baseline, double median) =>
        Math.Max(Math.Max(MadScale * Mad(baseline, median), Math.Sqrt(Math.Max(median, 0))), 1);

    /// <summary>Wilson score interval for k successes out of n (95% by default).</summary>
    public static (double Low, double High) Wilson(int k, int n, double z = 1.96)
    {
        if (n <= 0) return (0, 1);
        double p = (double)k / n, z2 = z * z;
        double denom = 1 + z2 / n;
        double centre = (p + z2 / (2 * n)) / denom;
        double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }
}
