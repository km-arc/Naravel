using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Naravel.Notifications;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration for <c>Naravel.Notifications</c>.</summary>
public static class NotificationServiceCollectionExtensions
{
    /// <summary>Registers <see cref="INotificationSender"/> (scoped, so scoped channels can resolve their dependencies).</summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> the notification service provider.</para>
    /// <para><b>Why it exists:</b> one explicit registration point over .NET DI; channels are added with <c>AddNotificationChannel</c>.</para>
    /// <para><b>Not ported on purpose:</b> channel discovery and a global facade.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddNaravelNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<INotificationSender, NotificationSender>();
        return services;
    }

    /// <summary>
    /// Registers a channel class for <typeparamref name="TNotification"/>. This is also how a channel is added at runtime: register before
    /// the provider is built, or add it to a scope's own collection in a test. Channel names must be unique per notification type.
    /// </summary>
    /// <typeparam name="TNotification">The notification type the channel delivers.</typeparam>
    /// <typeparam name="TChannel">The channel implementation, resolved from DI per scope.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddNotificationChannel<TNotification, TChannel>(this IServiceCollection services)
        where TChannel : class, INotificationChannel<TNotification>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationChannel<TNotification>, TChannel>());
        return services;
    }

    /// <summary>Registers a channel for <typeparamref name="TNotification"/> from a delegate, for a channel that needs no class of its own.</summary>
    /// <typeparam name="TNotification">The notification type the channel delivers.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The channel name callers pass to <see cref="INotificationSender"/>.</param>
    /// <param name="send">Delivers one notification.</param>
    public static IServiceCollection AddNotificationChannel<TNotification>(
        this IServiceCollection services,
        string name,
        Func<NotificationContext, TNotification, CancellationToken, Task> send)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(send);
        services.AddSingleton<INotificationChannel<TNotification>>(new DelegateNotificationChannel<TNotification>(name, send));
        return services;
    }

    /// <summary>
    /// Registers a channel that writes one log line per delivery with the notification and recipient <b>type names only</b>, never their contents.
    /// </summary>
    /// <typeparam name="TNotification">The notification type the channel delivers.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The channel name, <c>log</c> by default.</param>
    public static IServiceCollection AddLogNotificationChannel<TNotification>(this IServiceCollection services, string name = "log")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        services.AddSingleton<INotificationChannel<TNotification>>(provider => new LogNotificationChannel<TNotification>(
            name,
            provider.GetService<ILoggerFactory>()?.CreateLogger("Naravel.Notifications") ?? NullLogger.Instance));
        return services;
    }
}
