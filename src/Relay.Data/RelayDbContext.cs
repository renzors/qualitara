using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Relay.Data.Entities;

namespace Relay.Data;

/// <summary>EF model mirroring sql/squema.sql (table and column names, types and nullability).</summary>
public sealed class RelayDbContext(DbContextOptions<RelayDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    /// <summary>Timestamps are stored as UTC text; mark them UTC on read.</summary>
    private static readonly ValueConverter<DateTime, DateTime> Utc =
        new(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("INTEGER").ValueGeneratedNever();
            e.Property(x => x.Name).HasColumnName("name").HasColumnType("VARCHAR(120)").HasMaxLength(120).IsRequired();
            e.Property(x => x.Industry).HasColumnName("industry").HasColumnType("VARCHAR(60)").HasMaxLength(60).IsRequired();
            e.Property(x => x.Timezone).HasColumnName("timezone").HasColumnType("VARCHAR(60)").HasMaxLength(60).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("TIMESTAMP").HasConversion(Utc).IsRequired();
        });

        b.Entity<ActivityEvent>(e =>
        {
            e.ToTable("activity_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("INTEGER").ValueGeneratedNever();
            e.Property(x => x.AccountId).HasColumnName("account_id").HasColumnType("INTEGER").IsRequired();
            e.Property(x => x.Location).HasColumnName("location").HasColumnType("VARCHAR(80)").HasMaxLength(80).IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").HasColumnType("VARCHAR(40)").HasMaxLength(40).IsRequired();
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("TIMESTAMP").HasConversion(Utc).IsRequired();
            e.Property(x => x.DurationSeconds).HasColumnName("duration_seconds").HasColumnType("INTEGER");
            e.Property(x => x.Outcome).HasColumnName("outcome").HasColumnType("VARCHAR(40)").HasMaxLength(40);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.OccurredAt }).HasDatabaseName("ix_activity_events_account_occurred");
        });
    }
}
