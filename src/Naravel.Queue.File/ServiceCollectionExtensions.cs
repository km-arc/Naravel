using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;

namespace Naravel.Queue.File;

/// <summary>Registration extension methods for the file-based queue driver.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "file" driver for every configured store whose "Driver" is "file".
    /// Reads that store's own "Path" (default "storage/netqueue/{storeName}") and
    /// "VisibilityTimeoutSeconds" (default 300), which must be greater than zero.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the store is first resolved (not when this method runs) if its "VisibilityTimeoutSeconds" is zero, negative or NaN:
    /// the message names the store and the setting, and nothing is created on disk. A value that is not a number at all still throws
    /// <see cref="FormatException"/>, as before.
    /// </exception>
    public static IServiceCollection AddFileDriver(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        => services.AddQueueDriver(configuration, "file", (_, store) =>
        {
            var path = store.GetOrDefault("Path", Path.Combine("storage", "netqueue", store.Key));
            var configured = store.GetOrDefault("VisibilityTimeoutSeconds", "300");
            var visibilitySeconds = double.Parse(configured);
            if (!(visibilitySeconds > 0)) // written this way so that NaN is refused too
                throw new InvalidOperationException(
                    $"The File queue store '{store.Key}' has VisibilityTimeoutSeconds '{configured}', which is not greater than zero. " +
                    "Set it above the longest job; the default is 300.");
            return new FileQueueDriver(path, TimeSpan.FromSeconds(visibilitySeconds));
        }, sectionName);
}
