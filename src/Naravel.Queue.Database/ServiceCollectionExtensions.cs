using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;

namespace Naravel.Queue.Database;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "database" driver against the given DbContext type, for every configured store whose
    /// "Driver" is "database". You must separately register
    /// <c>services.AddDbContextFactory&lt;TContext&gt;(options => options.UseSqlServer/UseNpgsql/UseSqlite(...))</c>
    /// and add <c>modelBuilder.ConfigureQueueJobs();</c> to that context's OnModelCreating, then create a migration.
    /// Reads each matching store's own "VisibilityTimeoutSeconds" (default 300). The connection string itself
    /// lives on your DbContext, not in this configuration.
    /// </summary>
    /// <remarks>
    /// Known limitation: every store using the "database" driver against the same <typeparamref name="TContext"/>
    /// shares the same underlying table with no store/connection column, so two "database" stores are only
    /// useful if you also gave them different <c>DbContext</c> types (two calls to this method with different
    /// <typeparamref name="TContext"/>). See docs/en/queue.md "Limitations".
    /// </remarks>
    public static IServiceCollection AddDatabaseDriver<TContext>(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        where TContext : DbContext
        => services.AddQueueDriver(configuration, "database", (sp, store) =>
        {
            var contextFactory = sp.GetRequiredService<IDbContextFactory<TContext>>();
            var visibilitySeconds = double.Parse(store.GetOrDefault("VisibilityTimeoutSeconds", "300"));
            return new DatabaseQueueDriver<TContext>(contextFactory, TimeSpan.FromSeconds(visibilitySeconds));
        }, sectionName);
}
