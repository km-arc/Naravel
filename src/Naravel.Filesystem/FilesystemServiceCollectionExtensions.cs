namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Filesystem;
using Naravel.Filesystem.Drivers;
using Naravel.Foundation;

public static class FilesystemServiceCollectionExtensions
{
    /// <summary>Registers the filesystem manager and its configured local/S3 stores.</summary>
    public static IServiceCollection AddNaravelFilesystem(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = FilesystemOptions.Position)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        services.Configure<FilesystemOptions>(configuration.GetSection(sectionName));
        return AddFilesystemServices(services);
    }

    /// <summary>Registers the filesystem manager using programmatic options.</summary>
    public static IServiceCollection AddNaravelFilesystem(this IServiceCollection services, Action<FilesystemOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);
        services.Configure(configureOptions);
        return AddFilesystemServices(services);
    }

    private static IServiceCollection AddFilesystemServices(IServiceCollection services)
    {
        services.TryAddSingleton<IDriverRegistry<IStorageDriver>>(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<FilesystemOptions>>().CurrentValue;
            var registry = new DriverRegistry<IStorageDriver>();
            foreach (var (name, section) in options.Stores)
            {
                registry.Register(name, _ => CreateDriver(section));
            }

            return registry;
        });
        services.TryAddSingleton<StorageManager>();
        services.TryAddSingleton<IStorageDriver>(provider => provider.GetRequiredService<StorageManager>().Driver());

        return services;
    }

    private static IStorageDriver CreateDriver(IConfigurationSection section)
    {
        var options = new DiskOptions
        {
            Driver = section[nameof(DiskOptions.Driver)] ?? "local",
            Root = section[nameof(DiskOptions.Root)],
            BaseUrl = section[nameof(DiskOptions.BaseUrl)],
            Key = section[nameof(DiskOptions.Key)],
            Secret = section[nameof(DiskOptions.Secret)],
            Region = section[nameof(DiskOptions.Region)],
            Bucket = section[nameof(DiskOptions.Bucket)],
            Endpoint = section[nameof(DiskOptions.Endpoint)]
        };

        return options.Driver.ToLowerInvariant() switch
        {
            "local" => new LocalStorageDriver(options.Root ?? "wwwroot/uploads", options.BaseUrl ?? "/storage"),
            "s3" => new S3StorageDriver(
                options.Key ?? throw new ArgumentNullException(nameof(options.Key)),
                options.Secret ?? throw new ArgumentNullException(nameof(options.Secret)),
                options.Bucket ?? throw new ArgumentNullException(nameof(options.Bucket)),
                options.Endpoint,
                options.Region),
            _ => throw new NotSupportedException($"Driver [{options.Driver}] is not supported.")
        };
    }
}