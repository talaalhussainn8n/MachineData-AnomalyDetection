using Microsoft.EntityFrameworkCore;

namespace MachineMonitor.Api.Data;

public sealed class ReadingEntity
{
    public long Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Sensor { get; set; } = string.Empty;
    public double Value { get; set; }
    public bool IsAnomaly { get; set; }
    public double AnomalyScore { get; set; }

    /// <summary>Zeitstempel in UTC. DateTime statt DateTimeOffset, damit Sortierung auch unter SQLite funktioniert.</summary>
    public DateTime TimestampUtc { get; set; }
}

public sealed class AlarmEntity
{
    public long Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Sensor { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Score { get; set; }
    public string Detector { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public bool Acknowledged { get; set; }
}

public sealed class MonitorDbContext : DbContext
{
    public MonitorDbContext(DbContextOptions<MonitorDbContext> options) : base(options)
    {
    }

    public DbSet<ReadingEntity> Readings => Set<ReadingEntity>();
    public DbSet<AlarmEntity> Alarms => Set<AlarmEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReadingEntity>(e =>
        {
            e.Property(r => r.MachineId).HasMaxLength(64);
            e.Property(r => r.Sensor).HasMaxLength(32);
            e.HasIndex(r => new { r.MachineId, r.Sensor, r.TimestampUtc });
        });

        modelBuilder.Entity<AlarmEntity>(e =>
        {
            e.Property(a => a.MachineId).HasMaxLength(64);
            e.Property(a => a.Sensor).HasMaxLength(32);
            e.Property(a => a.Detector).HasMaxLength(64);
            e.Property(a => a.Message).HasMaxLength(512);
            e.HasIndex(a => a.TimestampUtc);
        });
    }
}
