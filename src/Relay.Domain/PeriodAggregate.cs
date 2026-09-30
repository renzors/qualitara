namespace Relay.Domain;

/// <summary>Everything the metrics need from one period, for one scope (account or location).</summary>
public sealed class PeriodAggregate
{
    public int Total, Calls, Leads, Appointments;
    public int CallsKnown, Missed, Voicemail;
    public int LeadsKnown, Converted;
    public int AppointmentsKnown, NoShow;
    public int NoOutcome;
    public int CallsNoDuration;
    public int OddHours;
    public readonly List<double> TalkSeconds = [];

    public static PeriodAggregate From(ReadOnlySpan<LocalEvent> events, string? location, bool[] rareHours)
    {
        var a = new PeriodAggregate();
        foreach (var le in events)
        {
            var e = le.Raw;
            if (location is not null && !string.Equals(e.Location, location, StringComparison.Ordinal)) continue;

            a.Total++;
            if (rareHours[le.Local.Hour]) a.OddHours++;
            if (e.Outcome is null) a.NoOutcome++;

            switch (e.Type)
            {
                case EventType.CallReceived:
                    a.Calls++;
                    if (e.DurationSeconds is null) a.CallsNoDuration++;
                    switch (e.Outcome)
                    {
                        case Outcomes.Connected:
                            a.CallsKnown++;
                            if (e.DurationSeconds is int d) a.TalkSeconds.Add(d);
                            break;
                        case Outcomes.Missed: a.CallsKnown++; a.Missed++; break;
                        case Outcomes.Voicemail: a.CallsKnown++; a.Voicemail++; break;
                    }
                    break;
                case EventType.LeadCreated:
                    a.Leads++;
                    if (e.Outcome is Outcomes.Open or Outcomes.Converted) a.LeadsKnown++;
                    if (e.Outcome == Outcomes.Converted) a.Converted++;
                    break;
                case EventType.AppointmentSet:
                    a.Appointments++;
                    if (e.Outcome is Outcomes.Completed or Outcomes.NoShow) a.AppointmentsKnown++;
                    if (e.Outcome == Outcomes.NoShow) a.NoShow++;
                    break;
            }
        }
        return a;
    }
}
