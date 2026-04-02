using Microsoft.EntityFrameworkCore;
using MQTTower.Infrastructure.Data.Entities;

namespace MQTTower.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<DeviceStateEntity> DeviceStates => Set<DeviceStateEntity>();
    public DbSet<MetricSnapshotEntity> MetricSnapshots => Set<MetricSnapshotEntity>();
    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();
    public DbSet<ScheduledTaskEntity> ScheduledTasks => Set<ScheduledTaskEntity>();
    public DbSet<TopicWatcherEntity> TopicWatchers => Set<TopicWatcherEntity>();
    public DbSet<NotificationRuleEntity> NotificationRules => Set<NotificationRuleEntity>();
    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
        });

        modelBuilder.Entity<DeviceStateEntity>(e =>
        {
            e.HasKey(x => x.DeviceId);
        });

        modelBuilder.Entity<MetricSnapshotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.CapturedAt });
        });

        modelBuilder.Entity<AuditEntryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<ScheduledTaskEntity>(e => e.HasKey(x => x.Id));
        modelBuilder.Entity<TopicWatcherEntity>(e => e.HasKey(x => x.Id));
        modelBuilder.Entity<NotificationRuleEntity>(e => e.HasKey(x => x.Id));

        modelBuilder.Entity<AppUserEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserName).IsUnique();
        });
    }
}
