using Microsoft.Extensions.Options;
using Naravel.Foundation;

namespace Naravel.Mail;

/// <summary>
/// Resolves named mail transports and hands out <see cref="IMailer"/> instances bound to them.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Mail\MailManager</c> (<c>mailer($name)</c>, <c>extend()</c>).</para>
/// <para><b>Why it exists:</b> choosing <c>log</c> in development, <c>smtp</c> in production and <c>array</c> in tests by name
/// is exactly the Manager to Driver job (PDR-013a); runtime <c>Extend</c> and configuration reload come from the base class.</para>
/// <para><b>Not ported on purpose:</b> <c>__call</c> forwarding. Inject <see cref="IMailer"/> for the default transport.</para>
/// </remarks>
public sealed class MailManager : Manager<IMailTransport, MailOptions>
{
    private readonly IServiceProvider _provider;
    private readonly IOptionsMonitor<MailOptions> _options;

    /// <summary>Creates the manager. Normally resolved from DI after <c>AddNaravelMail</c>.</summary>
    /// <param name="provider">Service provider handed to transport factories.</param>
    /// <param name="registry">The transport registry.</param>
    /// <param name="options">Live mail options.</param>
    public MailManager(IServiceProvider provider, IDriverRegistry<IMailTransport> registry, IOptionsMonitor<MailOptions> options)
        : base(provider, registry, options)
    {
        _provider = provider;
        _options = options;
    }

    /// <summary>
    /// Gets a mailer bound to a named transport. The transport is resolved on every send, so a changed <c>Mail:Default</c>
    /// or a reloaded transport takes effect without re-injecting.
    /// </summary>
    /// <param name="name">Transport name, or <see langword="null"/> for the default.</param>
    public IMailer Mailer(string? name = null) => new TransportMailer(this, name, _options, _provider);
}
