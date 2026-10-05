using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;

namespace Naravel.Queue.File;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "file" driver for every configured store whose "Driver" is "file".
    /// Reads that store's own "Path" (default "storage/netqueue/{storeName}") and
    /// "VisibilityTimeoutSeconds" (default 300).
    /// </summary>
    public static IServiceCollection AddFileDriver(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        => services.AddQueueDriver(configuration, "file", (_, store) =>
        {
            var path = store.GetOrDefault("Path", Path.Combine("storage", "netqueue", store.Key));
            var visibilitySeconds = double.Parse(store.GetOrDefault("VisibilityTimeoutSeconds", "300"));
            return new FileQueueDriver(path, TimeSpan.FromSeconds(visibilitySeconds));
        }, sectionName);
}
