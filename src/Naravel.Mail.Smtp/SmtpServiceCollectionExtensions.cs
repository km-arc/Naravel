using Microsoft.Extensions.Configuration;
using Naravel.Mail;
using Naravel.Mail.Smtp;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the optional SMTP transport for Naravel.Mail.</summary>
public static class SmtpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <c>smtp</c> driver: every <c>Mail:Mailers:&lt;name&gt;</c> with <c>Driver: smtp</c> becomes its own
    /// <see cref="SmtpMailTransport"/>, configured by the keys <c>Host</c>, <c>Port</c>, <c>Security</c>
    /// (<c>StartTls</c>, <c>SslOnConnect</c>, <c>None</c>), <c>Username</c>, <c>Password</c> and <c>Timeout</c> (seconds).
    /// Call it after <c>AddNaravelMail</c>.
    /// </summary>
    /// <remarks>
    /// <b>Laravel equivalent:</b> the <c>smtp</c> mailer. A missing <c>Host</c> or an invalid value fails when the transport is
    /// first used, with a message that names the key.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Mail</c> by default.</param>
    public static IServiceCollection AddNaravelMailSmtp(this IServiceCollection services, IConfiguration configuration, string sectionName = "Mail")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddMailTransport(configuration, "smtp", (_, section) => new SmtpMailTransport(ReadOptions(section)), sectionName);
    }

    internal static SmtpMailOptions ReadOptions(IConfigurationSection section)
    {
        var options = new SmtpMailOptions
        {
            Host = section["Host"] ?? string.Empty,
            Username = section["Username"],
            Password = section["Password"]
        };

        if (section["Port"] is { Length: > 0 } port)
        {
            options.Port = int.TryParse(port, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"Mail:Mailers:{section.Key}:Port '{port}' is not a number.");
        }

        if (section["Security"] is { Length: > 0 } security)
        {
            options.Security = Enum.TryParse<SmtpSecurity>(security, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
                ? mode
                : throw new InvalidOperationException($"Mail:Mailers:{section.Key}:Security '{security}' must be StartTls, SslOnConnect or None.");
        }

        if (section["Timeout"] is { Length: > 0 } timeout)
        {
            options.Timeout = double.TryParse(timeout, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : throw new InvalidOperationException($"Mail:Mailers:{section.Key}:Timeout '{timeout}' must be a positive number of seconds.");
        }

        return options;
    }
}
