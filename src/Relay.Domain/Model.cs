namespace Relay.Domain;

public enum EventType { CallReceived, LeadCreated, AppointmentSet }

public enum Sensitivity { Relaxed, Normal, Strict }

public enum MetricKind { Count, Rate, Duration }

public enum MetricStatus { Normal, WorthALook, Unusual, InsufficientData }

public enum Direction { Same, Higher, Lower }

public enum Polarity { Neutral, Good, Bad }

/// <summary>Verdicts in evaluation order (§6).</summary>
public enum Verdict { NoActivity, InsufficientHistory, Unusual, WorthALook, Normal }

public sealed record AccountInfo(int Id, string Name, string Industry, string TimeZoneId, DateTime CreatedAtUtc);

/// <summary>One raw activity row. Record equality is the dedupe key (§5.5): everything except the id.</summary>
public sealed record ActivityEvent(string Location, EventType Type, DateTime OccurredAtUtc, int? DurationSeconds, string? Outcome);

public sealed record AssessmentRequest(DateTime AsOfUtc, int WindowDays = 7, Sensitivity Sensitivity = Sensitivity.Normal)
{
    public static readonly int[] AllowedWindows = [1, 7, 30];
}

/// <summary>A half-open period (Start, End]: events strictly after Start and at or before End.</summary>
public sealed record Period(DateTime StartUtc, DateTime EndUtc)
{
    public bool Contains(DateTime t) => t > StartUtc && t <= EndUtc;
}

public static class EventTypes
{
    public static EventType? Parse(string value) => value switch
    {
        "call_received" => EventType.CallReceived,
        "lead_created" => EventType.LeadCreated,
        "appointment_set" => EventType.AppointmentSet,
        _ => null,
    };
}

public static class Outcomes
{
    public const string Connected = "connected", Missed = "missed", Voicemail = "voicemail";
    public const string Open = "open", Converted = "converted";
    public const string Completed = "completed", NoShow = "no_show";
}
