using Microsoft.EntityFrameworkCore;

namespace Naravel.Queue.Database;

public static class QueueModelBuilderExtensions
{
    /// <summary>
    /// Call from your DbContext's OnModelCreating to map the jobs table:
    /// <code>modelBuilder.ConfigureQueueJobs();</code>
    /// Then add <c>public DbSet&lt;JobRecord&gt; QueueJobs => Set&lt;JobRecord&gt;();</c> to your context
    /// and create/apply a migration.
    /// </summary>
    public static ModelBuilder ConfigureQueueJobs(this ModelBuilder modelBuilder, string tableName = "QueueJobs")
    {
        modelBuilder.Entity<JobRecord>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(j => j.Id);
            b.Property(j => j.Id).HasMaxLength(64);
            b.Property(j => j.Queue).HasMaxLength(128).IsRequired();
            b.Property(j => j.Connection).HasMaxLength(128);
            b.Property(j => j.TraceParent).HasMaxLength(128);
            b.Property(j => j.TraceState).HasMaxLength(512);
            b.Property(j => j.JobType).IsRequired();
            b.Property(j => j.Payload).IsRequired();
            b.HasIndex(j => new { j.Queue, j.ReservedAt, j.FailedAt, j.AvailableAt })
                .HasDatabaseName($"IX_{tableName}_Polling");
        });
        modelBuilder.ConfigureQueueFailedJobs();
        modelBuilder.ConfigureQueueBatches();
        return modelBuilder;
    }

    public static ModelBuilder ConfigureQueueFailedJobs(this ModelBuilder modelBuilder, string tableName = "QueueFailedJobs")
    {
        modelBuilder.Entity<FailedJobRecord>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(record => record.Id);
            b.Property(record => record.Id).HasMaxLength(128);
            b.Property(record => record.Envelope).IsRequired();
            b.Property(record => record.Error).IsRequired();
            b.HasIndex(record => record.FailedAt).HasDatabaseName($"IX_{tableName}_FailedAt");
        });
        return modelBuilder;
    }

    public static ModelBuilder ConfigureQueueBatches(this ModelBuilder modelBuilder, string tableName = "QueueBatches")
    {
        modelBuilder.Entity<QueueBatchRecord>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(batch => batch.Id);
            b.Property(batch => batch.Id).HasMaxLength(128);
            b.Property(batch => batch.ThenCallbacks).IsRequired();
            b.Property(batch => batch.CatchCallbacks).IsRequired();
            b.Property(batch => batch.FinallyCallbacks).IsRequired();
        });
        return modelBuilder;
    }
}
