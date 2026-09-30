namespace Relay.Data;

/// <summary>sql/seed.sql, embedded at build time so the repo's copy stays the single source of truth.</summary>
public static class SeedScript
{
    public const string ResourceName = "Relay.Data.seed.sql";

    public static string Read()
    {
        using var stream = typeof(SeedScript).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
