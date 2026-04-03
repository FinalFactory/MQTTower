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
    public DbSet<BrokerProfileEntity> BrokerProfiles => Set<BrokerProfileEntity>();
    public DbSet<RegistrationTokenEntity> RegistrationTokens => Set<RegistrationTokenEntity>();

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
            e.Property(x => x.LastPayloadPreview).HasMaxLength(2048);
            e.HasOne<DeviceEntity>()
                .WithMany()
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MetricSnapshotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasIndex(x => new { x.Name, x.CapturedAt });
        });

        modelBuilder.Entity<AuditEntryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(128);
            e.Property(x => x.EntityType).HasMaxLength(128);
            e.Property(x => x.EntityName).HasMaxLength(512);
            e.Property(x => x.Details).HasMaxLength(8192);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<ScheduledTaskEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.CronExpression).HasMaxLength(256);
            e.Property(x => x.Topic).HasMaxLength(1024);
            e.Property(x => x.Payload).HasMaxLength(65536);
        });

        modelBuilder.Entity<TopicWatcherEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.TopicPattern).HasMaxLength(512);
            e.Property(x => x.Condition).HasMaxLength(2048);
            e.Property(x => x.ActionType).HasMaxLength(64);
            e.Property(x => x.ActionConfigJson).HasMaxLength(8192);
        });

        modelBuilder.Entity<NotificationRuleEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.TriggerType).HasMaxLength(64);
            e.Property(x => x.ConfigJson).HasMaxLength(8192);
            e.Property(x => x.Channel).HasMaxLength(64);
        });

        modelBuilder.Entity<AppUserEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserName).IsUnique();
        });

        modelBuilder.Entity<BrokerProfileEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.AgentUrl).HasMaxLength(2048);
            e.HasIndex(x => x.UseLocalServices);
            e.HasIndex(x => x.AgentUrl)
                .IsUnique()
                .HasFilter("AgentUrl <> ''");
        });

        modelBuilder.Entity<RegistrationTokenEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasIndex(x => x.TokenHash).IsUnique();
        });
    }
}
