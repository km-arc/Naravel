using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Naravel.Foundation;
using Naravel.Mail;
using Naravel.Mail.Transports;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the mail module and lets transport packages add named transports.</summary>
public static class MailServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MailManager"/>, an <see cref="IMailer"/> for the default transport and the built-in <c>log</c> and
    /// <c>array</c> transports. Configure <c>Mail:Default</c>, <c>Mail:From</c> and <c>Mail:Mailers:&lt;name&gt;:Driver</c>.
    /// </summary>
    /// <remarks>
    /// <b>Laravel equivalent:</b> <c>MailServiceProvider</c>. Without any configuration the default transport is <c>log</c>,
    /// so nothing is delivered by accident.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Mail</c> by default.</param>
    public static IServiceCollection AddNaravelMail(this IServiceCollection services, IConfiguration configuration, string sectionName = "Mail")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.AddNaravelDriver<IMailTransport>("log", CreateLogTransport);
        services.AddNaravelDriver<IMailTransport>("array", _ => new ArrayMailTransport());
        services.AddMailTransport(configuration, "log", (provider, _) => CreateLogTransport(provider), sectionName);
        services.AddMailTransport(configuration, "array", (_, _) => new ArrayMailTransport(), sectionName);

        services.AddNaravelManager<MailManager, IMailTransport, MailOptions>(configuration.GetSection(sectionName));
        services.TryAddSingleton<IMailer>(provider => provider.GetRequiredService<MailManager>().Mailer());
        return services;
    }

    /// <summary>
    /// For transport packages: registers one factory for every <c>Mail:Mailers:&lt;name&gt;</c> whose <c>Driver</c> equals
    /// <paramref name="driverName"/>, so each configured name gets its own transport instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="driverName">The <c>Driver</c> value the factory handles, for example <c>smtp</c>.</param>
    /// <param name="factory">Creates the transport from the provider and the mailer's configuration section.</param>
    /// <param name="sectionName">The configuration section, <c>Mail</c> by default.</param>
    public static IServiceCollection AddMailTransport(
        this IServiceCollection services,
        IConfiguration configuration,
        string driverName,
        Func<IServiceProvider, IConfigurationSection, IMailTransport> factory,
        string sectionName = "Mail")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        ArgumentNullException.ThrowIfNull(factory);

        foreach (var mailer in configuration.GetSection($"{sectionName}:Mailers").GetChildren())
        {
            if (!string.Equals(mailer["Driver"], driverName, StringComparison.OrdinalIgnoreCase)) continue;
            var section = mailer;
            services.AddNaravelDriver<IMailTransport>(mailer.Key, provider => factory(provider, section));
        }

        return services;
    }

    private static IMailTransport CreateLogTransport(IServiceProvider provider) =>
        new LogMailTransport(provider.GetService<ILoggerFactory>()?.CreateLogger("Naravel.Mail") ?? NullLogger.Instance);
}
