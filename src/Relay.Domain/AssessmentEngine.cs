namespace Relay.Domain;

/// <summary>
/// Pure assessment engine: takes an account's events plus parameters and returns assessments. No I/O.
/// <paramref name="datasetStartUtc"/> on each call is the earliest instant the data covers; baseline periods
/// starting before it (or before the account was created) are dropped rather than counted as zero.
/// </summary>
public sealed class AssessmentEngine(EngineOptions? options = null)
{
    public EngineOptions Options { get; } = options ?? EngineOptions.Default;

    public AccountAssessment Assess(PreparedAccount account, AssessmentRequest request, DateTime datasetStartUtc, bool includeLocations = true)
    {
        var t = Options.ThresholdsFor(request.Sensitivity);
        var plan = PlanFor(account, request.AsOfUtc, request.WindowDays, datasetStartUtc);
        var rare = RareHours(account, plan);
        string rareText = Format.HourRanges(rare);
        int locationCount = account.Locations.Count;

        if (!account.HasAnyEvents)
        {
            var none = new ScopeAssessment(Verdict.NoActivity, [], [], "No activity on record.", 0, null);
            return new(account.Account, request, plan, none, new DataQuality(0, 0, 0, 0, 0, null), [], rare, rareText, 0);
        }

        var recentAgg = PeriodAggregate.From(account.In(plan.Recent), null, rare);
        var baseAggs = plan.Baseline.Select(p => PeriodAggregate.From(account.In(p), null, rare)).ToList();
        var overall = AssessScope(account, plan, recentAgg, baseAggs, null, rare, t);

        var quality = new DataQuality(account.DuplicatesRemoved, recentAgg.Total, recentAgg.NoOutcome, recentAgg.Calls,
            recentAgg.CallsNoDuration, BaselineNote(account, plan, baseAggs, t));

        var locations = includeLocations ? AssessLocations(account, plan, rare, t) : [];
        return new(account.Account, request, plan, overall, quality, locations, rare, rareText, locationCount);
    }

    /// <summary>Weekly points over the whole history, with week ends aligned to asOf (as the engine would have seen each week).</summary>
    public IReadOnlyList<WeekPoint> WeeklySeries(PreparedAccount account, AssessmentRequest request, DateTime datasetStartUtc,
        DateTime datasetEndUtc, string? location = null)
    {
        var week = TimeSpan.FromDays(7);
        var ends = new List<DateTime>();
        for (var e = request.AsOfUtc; e - week >= datasetStartUtc; e -= week) ends.Add(e);
        for (var e = request.AsOfUtc + week; e <= datasetEndUtc; e += week) ends.Add(e);
        ends.Sort();

        var t = Options.ThresholdsFor(request.Sensitivity);
        return ends.Select(end =>
        {
            var plan = PlanFor(account, end, 7, datasetStartUtc);
            var rare = RareHours(account, plan);
            var recentAgg = PeriodAggregate.From(account.In(plan.Recent), location, rare);
            var baseAggs = plan.Baseline.Select(p => PeriodAggregate.From(account.In(p), location, rare)).ToList();
            var scope = account.HasAnyEvents
                ? AssessScope(account, plan, recentAgg, baseAggs, location, rare, t)
                : new ScopeAssessment(Verdict.NoActivity, [], [], null, 0, null);
            return new WeekPoint(plan.Recent, scope.Verdict, PeakDayIn(account, plan.Recent, location), scope.Metrics);
        }).ToList();
    }

    /// <summary>Weekly account verdicts for the <paramref name="weeks"/> weeks ending at asOf, oldest first. Null = before the data starts.</summary>
    public IReadOnlyList<Verdict?> VerdictHistory(PreparedAccount account, AssessmentRequest request, DateTime datasetStartUtc, int weeks = 25)
    {
        var t = Options.ThresholdsFor(request.Sensitivity);
        var result = new List<Verdict?>(weeks);
        for (int k = weeks - 1; k >= 0; k--)
        {
            var end = request.AsOfUtc - TimeSpan.FromDays(7 * k);
            if (end - TimeSpan.FromDays(7) < datasetStartUtc) { result.Add(null); continue; }
            if (!account.HasAnyEvents) { result.Add(Verdict.NoActivity); continue; }

            var plan = PlanFor(account, end, 7, datasetStartUtc);
            var rare = RareHours(account, plan);
            var recentAgg = PeriodAggregate.From(account.In(plan.Recent), null, rare);
            var baseAggs = plan.Baseline.Select(p => PeriodAggregate.From(account.In(p), null, rare)).ToList();
            result.Add(AssessScope(account, plan, recentAgg, baseAggs, null, rare, t).Verdict);
        }
        return result;
    }

    /// <summary>Weekday × local-hour grid: recent counts next to the baseline average per period.</summary>
    public Heatmap Heatmap(PreparedAccount account, AssessmentRequest request, DateTime datasetStartUtc, string? location = null)
    {
        var plan = PlanFor(account, request.AsOfUtc, request.WindowDays, datasetStartUtc);
        var rare = RareHours(account, plan);
        var recent = Grid<int>();
        var baseline = Grid<double>();

        int total = 0, rareCount = 0;
        foreach (var e in account.In(plan.Recent))
        {
            if (location is not null && e.Raw.Location != location) continue;
            recent[(int)e.Local.DayOfWeek][e.Local.Hour]++;
            total++;
            if (rare[e.Local.Hour]) rareCount++;
        }
        foreach (var p in plan.Baseline)
            foreach (var e in account.In(p))
                if (location is null || e.Raw.Location == location)
                    baseline[(int)e.Local.DayOfWeek][e.Local.Hour]++;

        if (plan.Baseline.Count > 0)
            foreach (var row in baseline)
                for (int h = 0; h < 24; h++) row[h] /= plan.Baseline.Count;

        return new Heatmap(recent, baseline, rare, Format.HourRanges(rare), plan.Baseline.Count, total, rareCount);
    }

    public PeriodPlan PlanFor(PreparedAccount account, DateTime asOfUtc, int windowDays, DateTime datasetStartUtc)
    {
        var earliest = account.Account.CreatedAtUtc > datasetStartUtc ? account.Account.CreatedAtUtc : datasetStartUtc;
        return PeriodPlan.Build(asOfUtc, windowDays, earliest, Options);
    }

    /// <summary>Local hours holding less than the rare-hour share of the account's baseline events (§5.4).</summary>
    public bool[] RareHours(PreparedAccount account, PeriodPlan plan)
    {
        var counts = new int[24];
        int total = 0;
        foreach (var p in plan.Baseline)
            foreach (var e in account.In(p)) { counts[e.Local.Hour]++; total++; }

        var rare = new bool[24];
        if (total == 0) return rare;
        for (int h = 0; h < 24; h++) rare[h] = (double)counts[h] / total < Options.RareHourShare;
        return rare;
    }

    private ScopeAssessment AssessScope(PreparedAccount account, PeriodPlan plan, PeriodAggregate recent, List<PeriodAggregate> baseline,
        string? location, bool[] rare, Thresholds t)
    {
        var o = Options;
        string rareText = Format.HourRanges(rare);
        var metrics = new List<MetricResult>
        {
            MetricCalculator.Count(Metrics.Total, recent.Total, baseline.Select(b => b.Total).ToList(), t, o),
            MetricCalculator.Count(Metrics.Calls, recent.Calls, baseline.Select(b => b.Calls).ToList(), t, o),
            MetricCalculator.Count(Metrics.Leads, recent.Leads, baseline.Select(b => b.Leads).ToList(), t, o),
            MetricCalculator.Count(Metrics.Appointments, recent.Appointments, baseline.Select(b => b.Appointments).ToList(), t, o),
            MetricCalculator.Rate(Metrics.MissedRate, recent.Missed, recent.CallsKnown, baseline.Select(b => (b.Missed, b.CallsKnown)).ToList(), t, o),
            MetricCalculator.Rate(Metrics.VoicemailRate, recent.Voicemail, recent.CallsKnown, baseline.Select(b => (b.Voicemail, b.CallsKnown)).ToList(), t, o),
            MetricCalculator.Rate(Metrics.Conversion, recent.Converted, recent.LeadsKnown, baseline.Select(b => (b.Converted, b.LeadsKnown)).ToList(), t, o),
            MetricCalculator.Rate(Metrics.NoShowRate, recent.NoShow, recent.AppointmentsKnown, baseline.Select(b => (b.NoShow, b.AppointmentsKnown)).ToList(), t, o),
            MetricCalculator.Duration(Metrics.TalkTime, recent.TalkSeconds, baseline.Select(b => (IReadOnlyList<double>)b.TalkSeconds).ToList(), plan.MinBaselineRequired, t, o),
            MetricCalculator.Rate(Metrics.OddHours, recent.OddHours, recent.Total, baseline.Select(b => (b.OddHours, b.Total)).ToList(), t, o,
                OddHoursDetail(account, plan.Recent, location, rare, rareText)),
            MetricCalculator.Rate(Metrics.NoOutcome, recent.NoOutcome, recent.Total, baseline.Select(b => (b.NoOutcome, b.Total)).ToList(), t, o),
        };

        double? expectedTotal = baseline.Count > 0 ? Stats.Median(baseline.Select(b => (double)b.Total)) : null;
        string periodWord = plan.Recent.EndUtc - plan.Recent.StartUtc == TimeSpan.FromDays(7) ? "week" : "period";

        string? why = null;
        bool lowVolume = false;
        if (!plan.HasEnoughPeriods)
            why = $"only {plan.Baseline.Count} of {plan.BaselineRequested} comparison periods available, needs {plan.MinBaselineRequired}";
        else if (expectedTotal < o.MinBaselineMedianVolume)
        {
            why = $"usually fewer than {o.MinBaselineMedianVolume:0} events per {periodWord}";
            lowVolume = true;
        }

        if (why is not null)
        {
            // D5: in a low-volume scope, a large surge in total volume is still a finding, not noise.
            var total = metrics[0];
            bool surge = lowVolume && total.Status == MetricStatus.Unusual && total.Direction == Direction.Higher
                         && recent.Total >= o.LowVolumeSurgeMinEvents;
            var shown = metrics
                .Select(m => m.Status == MetricStatus.InsufficientData || (surge && m == total) ? m : MetricCalculator.AsInsufficient(m, "not enough history"))
                .ToList();
            return surge
                ? new ScopeAssessment(Verdict.Unusual, shown, [total.Reason], $"Low volume: only a surge in total activity is assessed ({why}).", recent.Total, expectedTotal)
                : new ScopeAssessment(Verdict.InsufficientHistory, shown, [], $"Not enough history: {why}.", recent.Total, expectedTotal);
        }

        var flagged = metrics.Where(m => m.IsFlagged).OrderByDescending(m => Math.Abs(m.Z ?? 0)).ToList();
        var verdict = flagged.Any(m => m.Status == MetricStatus.Unusual) ? Verdict.Unusual
            : flagged.Count > 0 ? Verdict.WorthALook
            : Verdict.Normal;

        return new ScopeAssessment(verdict, metrics, flagged.Take(3).Select(m => m.Reason).ToList(), null, recent.Total, expectedTotal);
    }

    /// <summary>Names the rare hours that actually saw recent activity, or the whole rare range when none did.</summary>
    private static string? OddHoursDetail(PreparedAccount account, Period recent, string? location, bool[] rare, string rareText)
    {
        if (rareText.Length == 0) return null;
        var hit = new bool[24];
        foreach (var e in account.In(recent))
            if ((location is null || e.Raw.Location == location) && rare[e.Local.Hour]) hit[e.Local.Hour] = true;
        var used = Format.HourRanges(hit);
        return used.Length > 0 ? used : rareText;
    }

    private List<LocationAssessment> AssessLocations(PreparedAccount account, PeriodPlan plan, bool[] rare, Thresholds t)
    {
        var list = new List<LocationAssessment>();
        foreach (var loc in account.Locations)
        {
            var recent = PeriodAggregate.From(account.In(plan.Recent), loc, rare);
            var baseline = plan.Baseline.Select(p => PeriodAggregate.From(account.In(p), loc, rare)).ToList();
            int baseTotal = baseline.Sum(b => b.Total);
            if (recent.Total == 0 && baseTotal == 0) continue;

            var scope = AssessScope(account, plan, recent, baseline, loc, rare, t);
            bool isNew = baseTotal == 0 && recent.Total > 0;
            if (isNew)
                scope = scope with { Verdict = Verdict.InsufficientHistory, Reasons = [], Note = "New location: no activity in the comparison periods." };
            list.Add(new LocationAssessment(loc, isNew, scope));
        }
        return list
            .OrderBy(l => Verdicts.Rank(l.Assessment.Verdict))
            .ThenByDescending(l => l.Assessment.MaxAbsZ)
            .ThenBy(l => l.Location, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Explains a past spike that sits inside the baseline, which the median-based range ignores (§8.5).</summary>
    private string? BaselineNote(PreparedAccount account, PeriodPlan plan, List<PeriodAggregate> baseline, Thresholds t)
    {
        if (baseline.Count < 2) return null;
        var totals = baseline.Select(b => (double)b.Total).ToArray();
        double median = Stats.Median(totals), spread = Stats.CountSpread(totals, median);

        int worst = -1; double worstZ = t.Unusual;
        for (int i = 0; i < totals.Length; i++)
        {
            double z = (totals[i] - median) / spread;
            if (z >= worstZ) { worstZ = z; worst = i; }
        }
        if (worst < 0) return null;

        var p = plan.Baseline[worst];
        var from = TimeZones.ToLocal(p.StartUtc, account.TimeZone);
        var to = TimeZones.ToLocal(p.EndUtc, account.TimeZone);
        var peak = PeakDayIn(account, p, null);
        string peakText = peak is not null && peak.Count * 2 >= totals[worst] ? $", including {peak.Count} on {peak.Date:MMM d}" : "";
        string span = from.Date == to.Date || p.EndUtc - p.StartUtc <= TimeSpan.FromDays(1) ? $"{to:MMM d}" : $"{from:MMM d}–{to:MMM d}";
        return $"The comparison periods include an unusually busy one ({span}: {totals[worst]:0} events{peakText}, versus a usual {median:0}). " +
               "The usual range is median-based, so this spike does not widen it.";
    }

    /// <summary>The busiest account-local day in a period, for annotating spikes.</summary>
    public static PeakDay? PeakDayIn(PreparedAccount account, Period period, string? location)
    {
        var counts = new Dictionary<DateOnly, int>();
        foreach (var e in account.In(period))
        {
            if (location is not null && e.Raw.Location != location) continue;
            var d = DateOnly.FromDateTime(e.Local);
            counts[d] = counts.GetValueOrDefault(d) + 1;
        }
        if (counts.Count == 0) return null;
        var best = counts.MaxBy(kv => kv.Value);
        return new PeakDay(best.Key, best.Value);
    }

    private static T[][] Grid<T>() => Enumerable.Range(0, 7).Select(_ => new T[24]).ToArray();
}
