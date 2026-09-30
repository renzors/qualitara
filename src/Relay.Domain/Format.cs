using System.Globalization;

namespace Relay.Domain;

/// <summary>
/// Display strings for values and usual ranges. Ranges are rounded inward (lower bound up, upper bound down)
/// and a flagged value is rounded away from its range, so a flagged value never appears inside the range it broke.
/// </summary>
public static class Format
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private const double Eps = 1e-9;

    public static MetricDisplay Display(MetricKind kind, double? value, double? expected, double? low, double? high, Direction flaggedDirection)
    {
        string v = value is double x ? Value(kind, x, flaggedDirection) : "–";
        string? e = expected is double m ? Value(kind, m, Direction.Same) : null;
        if (low is not double lo || high is not double hi) return new(v, null, null, e);

        var (l, h) = kind switch
        {
            MetricKind.Count => InwardInt(Math.Max(0, lo), hi),
            MetricKind.Rate => InwardPercent(Math.Max(0, lo), Math.Min(1, hi)),
            _ => InwardDuration(Math.Max(0, lo), hi),
        };
        return new(v, l, h, e);
    }

    public static string Value(MetricKind kind, double v, Direction flagged = Direction.Same) => kind switch
    {
        MetricKind.Count => Round(v, flagged).ToString("0", C),
        MetricKind.Rate => Percent(v, flagged),
        _ => Duration(Round(v, flagged)),
    };

    public static string Duration(double seconds)
    {
        int s = (int)Math.Round(seconds);
        return s < 60 ? $"{s}s" : $"{s / 60}m {s % 60:00}s";
    }

    public static string Percent(double share, Direction flagged = Direction.Same)
    {
        double pct = share * 100;
        double whole = Round(pct, flagged);
        // Keep one decimal when the value is below 1% but not zero, so "0%" never hides real activity.
        if (whole == 0 && pct > Eps) return (Math.Ceiling(pct * 10) / 10).ToString("0.0", C) + "%";
        return whole.ToString("0", C) + "%";
    }

    private static double Round(double v, Direction flagged) => flagged switch
    {
        Direction.Higher => Math.Ceiling(v - Eps),
        Direction.Lower => Math.Floor(v + Eps),
        _ => Math.Round(v, MidpointRounding.AwayFromZero),
    };

    private static (string, string) InwardInt(double lo, double hi)
    {
        double l = Math.Ceiling(lo - Eps) + 0.0, h = Math.Floor(hi + Eps) + 0.0;
        if (l > h) { l = Math.Round(lo); h = l; }
        return (l.ToString("0", C), h.ToString("0", C));
    }

    private static (string, string) InwardPercent(double lo, double hi)
    {
        double l = Math.Ceiling(lo * 100 - Eps) + 0.0, h = Math.Floor(hi * 100 + Eps) + 0.0;
        if (l <= h) return (l.ToString("0", C) + "%", h.ToString("0", C) + "%");
        l = Math.Ceiling(lo * 1000 - Eps) / 10 + 0.0; h = Math.Floor(hi * 1000 + Eps) / 10 + 0.0;
        return (l.ToString("0.0", C) + "%", h.ToString("0.0", C) + "%");
    }

    private static (string, string) InwardDuration(double lo, double hi) =>
        (Duration(Math.Ceiling(lo - Eps)), Duration(Math.Floor(hi + Eps)));

    /// <summary>Contiguous runs of flagged local hours, wrapping midnight, e.g. "8pm–3am".</summary>
    public static string HourRanges(bool[] hours)
    {
        if (hours.All(h => !h)) return "";
        if (hours.All(h => h)) return "all hours";

        var runs = new List<string>();
        int start = Enumerable.Range(0, 24).First(i => hours[i] && !hours[(i + 23) % 24]);
        for (int n = 0, i = start; n < 24; )
        {
            if (!hours[i]) { i = (i + 1) % 24; n++; continue; }
            int from = i;
            while (n < 24 && hours[i]) { i = (i + 1) % 24; n++; }
            runs.Add($"{Hour(from)}–{Hour(i)}");
        }
        return string.Join(", ", runs);
    }

    public static string Hour(int h) => h switch
    {
        0 => "12am",
        12 => "12pm",
        < 12 => $"{h}am",
        _ => $"{h - 12}pm",
    };
}
