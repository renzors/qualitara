namespace Relay.Data.Entities;

public sealed class ActivityEvent
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string Location { get; set; } = "";
    /// <summary>'call_received' | 'lead_created' | 'appointment_set'</summary>
    public string EventType { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Outcome { get; set; }
}
