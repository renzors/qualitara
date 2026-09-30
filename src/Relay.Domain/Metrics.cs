namespace Relay.Domain;

/// <summary>A signal the engine assesses. <see cref="HigherIs"/> drives polarity labelling (§5.6).</summary>
public sealed record MetricDefinition(string Key, string Label, MetricKind Kind, Polarity HigherIs, string SampleUnit);

public static class Metrics
{
    public static readonly MetricDefinition Total = new("total", "All activity", MetricKind.Count, Polarity.Good, "events");
    public static readonly MetricDefinition Calls = new("calls", "Calls", MetricKind.Count, Polarity.Good, "calls");
    public static readonly MetricDefinition Leads = new("leads", "Leads", MetricKind.Count, Polarity.Good, "leads");
    public static readonly MetricDefinition Appointments = new("appointments", "Appointments", MetricKind.Count, Polarity.Good, "appointments");
    public static readonly MetricDefinition MissedRate = new("missed_rate", "Missed-call rate", MetricKind.Rate, Polarity.Bad, "calls with an outcome");
    public static readonly MetricDefinition VoicemailRate = new("voicemail_rate", "Voicemail rate", MetricKind.Rate, Polarity.Bad, "calls with an outcome");
    public static readonly MetricDefinition Conversion = new("conversion_rate", "Lead conversion", MetricKind.Rate, Polarity.Good, "leads with an outcome");
    public static readonly MetricDefinition NoShowRate = new("no_show_rate", "No-show rate", MetricKind.Rate, Polarity.Bad, "appointments with an outcome");
    public static readonly MetricDefinition TalkTime = new("talk_time", "Median talk time", MetricKind.Duration, Polarity.Neutral, "connected calls");
    public static readonly MetricDefinition OddHours = new("odd_hours", "Activity at unusual hours", MetricKind.Rate, Polarity.Neutral, "events");
    public static readonly MetricDefinition NoOutcome = new("no_outcome", "Events with no outcome", MetricKind.Rate, Polarity.Bad, "events");

    public static readonly IReadOnlyList<MetricDefinition> All =
        [Total, Calls, Leads, Appointments, MissedRate, VoicemailRate, Conversion, NoShowRate, TalkTime, OddHours, NoOutcome];

    public static MetricDefinition? Find(string key) => All.FirstOrDefault(m => m.Key == key);
}

public sealed record MetricDisplay(string Value, string? Low, string? High, string? Expected);

public sealed record MetricResult(
    MetricDefinition Definition,
    MetricStatus Status,
    double? Value,
    double? Expected,
    double? ExpectedLow,
    double? ExpectedHigh,
    double? Z,
    Direction Direction,
    Polarity Polarity,
    int SampleSize,
    double? ValueLow,
    double? ValueHigh,
    MetricDisplay Display,
    string Reason,
    string? Detail)
{
    public bool IsFlagged => Status is MetricStatus.Unusual or MetricStatus.WorthALook;
}
