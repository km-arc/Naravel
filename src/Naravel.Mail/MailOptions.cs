using Microsoft.Extensions.Configuration;
using Naravel.Foundation;

namespace Naravel.Mail;

/// <summary>
/// Configuration of the mail module: the default transport, the default sender and the named transports.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>config/mail.php</c> (<c>default</c>, <c>mailers</c>, <c>from</c>).</para>
/// <para><b>Why it exists:</b> binds <c>Mail:Default</c>, <c>Mail:From</c> and <c>Mail:Mailers:&lt;name&gt;</c> and feeds the
/// shared <c>Manager</c>. <see cref="Mailers"/> is the same dictionary the manager reads as <c>Stores</c>.</para>
/// </remarks>
public sealed class MailOptions : ManagerOptions
{
    /// <summary>Creates options whose default transport is <c>log</c> (no network).</summary>
    public MailOptions() => Default = "log";

    /// <summary>The sender used when a <see cref="Mailable"/> does not call <c>From(...)</c>.</summary>
    public string? From { get; set; }

    /// <summary>The named transports: <c>Mail:Mailers:&lt;name&gt;:Driver</c> selects <c>log</c>, <c>array</c> or <c>smtp</c>.</summary>
    public Dictionary<string, IConfigurationSection> Mailers
    {
        get => Stores;
        set => Stores = value;
    }
}
