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
            b.Property(j => j.JobType).IsRequired();
            b.Property(j => j.Payload).IsRequired();
            b.HasIndex(j => new { j.Queue, j.ReservedAt, j.FailedAt, j.AvailableAt })
                .HasDatabaseName($"IX_{tableName}_Polling");
        });
        return modelBuilder;
    }
}
