namespace Relay.Domain;

/// <summary>The assessment of one scope: a whole account or one of its locations.</summary>
public sealed record ScopeAssessment(
    Verdict Verdict,
    IReadOnlyList<MetricResult> Metrics,
    IReadOnlyList<string> Reasons,
    string? Note,
    int RecentTotal,
    double? ExpectedTotal)
{
    public MetricResult? Metric(string key) => Metrics.FirstOrDefault(m => m.Definition.Key == key);

    public double MaxAbsZ => Metrics.Where(m => m.Z is not null && m.Status != MetricStatus.InsufficientData)
        .Select(m => Math.Abs(m.Z!.Value)).DefaultIfEmpty(0).Max();
}

public sealed record LocationAssessment(string Location, bool IsNew, ScopeAssessment Assessment);

public sealed record DataQuality(
    int DuplicatesRemoved,
    int RecentEvents,
    int RecentNoOutcome,
    int RecentCalls,
    int RecentCallsNoDuration,
    string? BaselineNote);

public sealed record AccountAssessment(
    AccountInfo Account,
    AssessmentRequest Request,
    PeriodPlan Plan,
    ScopeAssessment Overall,
    DataQuality Quality,
    IReadOnlyList<LocationAssessment> Locations,
    bool[] RareHours,
    string RareHoursText,
    int LocationCount);

public sealed record PeakDay(DateOnly Date, int Count);

public sealed record WeekPoint(Period Period, Verdict Verdict, PeakDay? PeakDay, IReadOnlyList<MetricResult> Metrics);

public sealed record Heatmap(
    int[][] Recent,
    double[][] BaselineAverage,
    bool[] RareHours,
    string RareHoursText,
    int BaselinePeriods,
    int RecentTotal,
    int RecentRare);

public static class Verdicts
{
    /// <summary>Sort order for lists: most urgent first, then states without a verdict.</summary>
    public static int Rank(Verdict v) => v switch
    {
        Verdict.Unusual => 0,
        Verdict.WorthALook => 1,
        Verdict.Normal => 2,
        Verdict.InsufficientHistory => 3,
        _ => 4,
    };
}
