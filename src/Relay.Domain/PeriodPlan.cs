namespace Relay.Domain;

/// <summary>The recent period and its usable baseline periods (§4).</summary>
public sealed record PeriodPlan(Period Recent, IReadOnlyList<Period> Baseline, int BaselineRequested, int MinBaselineRequired)
{
    public bool HasEnoughPeriods => Baseline.Count >= MinBaselineRequired;

    /// <summary>
    /// W=1 compares against the same weekday in each of the prior 8 weeks; W=7 against the prior 8 weeks;
    /// W=30 against the prior 4 thirty-day periods. A baseline period is usable only if it starts at or after
    /// <paramref name="earliestUsable"/> (dataset start and account creation). Unusable periods are dropped, not zero-filled.
    /// </summary>
    public static PeriodPlan Build(DateTime asOfUtc, int windowDays, DateTime earliestUsable, EngineOptions options)
    {
        if (!AssessmentRequest.AllowedWindows.Contains(windowDays))
            throw new ArgumentOutOfRangeException(nameof(windowDays), windowDays, "Window must be 1, 7 or 30 days.");

        var length = TimeSpan.FromDays(windowDays);
        var step = TimeSpan.FromDays(windowDays == 1 ? 7 : windowDays);
        int requested = options.BaselinePeriodsFor(windowDays);

        var recent = new Period(asOfUtc - length, asOfUtc);
        var baseline = new List<Period>(requested);
        for (int k = 1; k <= requested; k++)
        {
            var end = asOfUtc - step * k;
            var p = new Period(end - length, end);
            if (p.StartUtc >= earliestUsable) baseline.Add(p);
        }
        return new PeriodPlan(recent, baseline, requested, options.MinBaselinePeriodsFor(windowDays));
    }
}
