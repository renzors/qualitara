using Relay.Domain;

namespace Relay.Api;

public sealed record PeriodDto(DateTime Start, DateTime End)
{
    public static PeriodDto From(Period p) => new(p.StartUtc, p.EndUtc);
}

public sealed record MetricDto(
    string Key, string Label, MetricKind Kind, MetricStatus Status,
    double? Value, double? Expected, double? ExpectedLow, double? ExpectedHigh, double? Z,
    Direction Direction, Polarity Polarity, int SampleSize, string SampleUnit,
    double? ValueLow, double? ValueHigh, MetricDisplay Display, string Reason, string? Detail)
{
    public static MetricDto From(MetricResult m) => new(
        m.Definition.Key, m.Definition.Label, m.Definition.Kind, m.Status,
        m.Value, m.Expected, m.ExpectedLow, m.ExpectedHigh, m.Z is double z ? Math.Round(z, 2) : null,
        m.Direction, m.Polarity, m.SampleSize, m.Definition.SampleUnit, m.ValueLow, m.ValueHigh, m.Display, m.Reason, m.Detail);
}

public sealed record ScopeDto(Verdict Verdict, string? Note, IReadOnlyList<string> Reasons, int RecentTotal, double? ExpectedTotal, IReadOnlyList<MetricDto> Metrics)
{
    public static ScopeDto From(ScopeAssessment s) =>
        new(s.Verdict, s.Note, s.Reasons, s.RecentTotal, s.ExpectedTotal, s.Metrics.Select(MetricDto.From).ToList());
}

public sealed record AccountDto(int Id, string Name, string Industry, string Timezone, DateTime CreatedAt, int LocationCount);

public sealed record AccountListItemDto(
    AccountDto Account, Verdict Verdict, string? Note, IReadOnlyList<string> Reasons, int RecentTotal, double? ExpectedTotal,
    IReadOnlyList<MetricDto> Metrics, IReadOnlyList<Verdict?> History, IReadOnlyList<Verdict> LocationVerdicts);

public sealed record AccountListDto(ParamsDto Params, PeriodDto Recent, int BaselinePeriods, IReadOnlyList<AccountListItemDto> Accounts);

public sealed record ParamsDto(DateTime AsOf, int Window, Sensitivity Sensitivity, double LookAt, double UnusualAt);

public sealed record LocationDto(string Location, bool IsNew, ScopeDto Assessment);

public sealed record DataQualityDto(int DuplicatesRemoved, int RecentEvents, int RecentNoOutcome, int RecentCalls, int RecentCallsNoDuration, string? BaselineNote);

public sealed record AssessmentDto(
    AccountDto Account, ParamsDto Params, PeriodDto Recent, IReadOnlyList<PeriodDto> Baseline, int BaselineRequested,
    ScopeDto Overall, DataQualityDto DataQuality, bool[] RareHours, string RareHoursText, IReadOnlyList<LocationDto> Locations);

public sealed record SeriesPointDto(double? Value, double? Expected, double? Low, double? High, double? Z, MetricStatus Status, string Display);

public sealed record MetricSeriesDto(string Key, string Label, MetricKind Kind, IReadOnlyList<SeriesPointDto> Points);

public sealed record WeekDto(DateTime Start, DateTime End, Verdict Verdict, bool IsCurrent, PeakDay? PeakDay);

public sealed record SeriesDto(ParamsDto Params, string? Location, IReadOnlyList<WeekDto> Weeks, IReadOnlyList<MetricSeriesDto> Series);

public sealed record HeatmapDto(ParamsDto Params, string? Location, int[][] Recent, double[][] BaselineAverage, bool[] RareHours,
    string RareHoursText, int BaselinePeriods, int RecentTotal, int RecentRare);

public sealed record MetaDto(DateTime FirstEventAt, DateTime LastEventAt, DateTime MinAsOf, DateTime MaxAsOf, DateTime DefaultAsOf,
    int[] Windows, int DefaultWindow, Sensitivity[] Sensitivities, Sensitivity DefaultSensitivity);
