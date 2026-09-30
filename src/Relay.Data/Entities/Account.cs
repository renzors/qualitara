namespace Relay.Data.Entities;

public sealed class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Industry { get; set; } = "";
    /// <summary>IANA timezone, e.g. "America/Chicago".</summary>
    public string Timezone { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
