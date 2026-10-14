namespace Naravel.Mail;

/// <summary>
/// An immutable, explicit e-mail message: sender, recipients, subject and a text and/or HTML body.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the data a <c>Mailable</c> resolves to after <c>build()</c>.</para>
/// <para><b>Why it exists:</b> a plain data type is serializable for the queue adapter (no delegates, no CLR type names),
/// comparable in tests and independent of any transport library.</para>
/// <para><b>Not ported on purpose:</b> attachments, inline images and template rendering (PDR-013a defers them).</para>
/// <para>Addresses are validated here, at the boundary: an invalid sender or recipient throws <see cref="ArgumentException"/>.
/// <c>Name &lt;user@example.com&gt;</c> is accepted.</para>
/// </remarks>
public sealed class MailMessage
{
    /// <summary>Creates a message with a single recipient.</summary>
    /// <param name="from">Sender address.</param>
    /// <param name="to">Recipient address.</param>
    /// <param name="subject">Subject line (may be empty, not null).</param>
    /// <param name="textBody">Plain-text body, or <see langword="null"/> when only <paramref name="htmlBody"/> is given.</param>
    /// <param name="htmlBody">Optional HTML body.</param>
    public MailMessage(string from, string to, string subject, string? textBody, string? htmlBody = null)
        : this(from, new[] { to }, subject, textBody, htmlBody, null, null)
    {
    }

    /// <summary>Creates a message with several recipients, optionally with Cc and Bcc.</summary>
    /// <param name="from">Sender address.</param>
    /// <param name="to">At least one recipient address.</param>
    /// <param name="subject">Subject line (may be empty, not null).</param>
    /// <param name="textBody">Plain-text body, or <see langword="null"/> when only <paramref name="htmlBody"/> is given.</param>
    /// <param name="htmlBody">Optional HTML body.</param>
    /// <param name="cc">Optional carbon-copy addresses.</param>
    /// <param name="bcc">Optional blind carbon-copy addresses.</param>
    public MailMessage(
        string from,
        IEnumerable<string> to,
        string subject,
        string? textBody,
        string? htmlBody = null,
        IEnumerable<string>? cc = null,
        IEnumerable<string>? bcc = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(subject);

        From = ValidateAddress(from, nameof(from));
        To = ValidateAddresses(to, nameof(to));
        if (To.Count == 0)
        {
            throw new ArgumentException("A mail message needs at least one recipient.", nameof(to));
        }

        Cc = ValidateAddresses(cc ?? Array.Empty<string>(), nameof(cc));
        Bcc = ValidateAddresses(bcc ?? Array.Empty<string>(), nameof(bcc));
        Subject = subject;
        TextBody = string.IsNullOrEmpty(textBody) ? null : textBody;
        HtmlBody = string.IsNullOrEmpty(htmlBody) ? null : htmlBody;
        if (TextBody is null && HtmlBody is null)
        {
            throw new ArgumentException("A mail message needs a text body, an HTML body, or both.", nameof(textBody));
        }
    }

    /// <summary>Sender address.</summary>
    public string From { get; }

    /// <summary>Recipient addresses (at least one).</summary>
    public IReadOnlyList<string> To { get; }

    /// <summary>Carbon-copy addresses.</summary>
    public IReadOnlyList<string> Cc { get; }

    /// <summary>Blind carbon-copy addresses.</summary>
    public IReadOnlyList<string> Bcc { get; }

    /// <summary>Subject line.</summary>
    public string Subject { get; }

    /// <summary>Plain-text body, or <see langword="null"/>.</summary>
    public string? TextBody { get; }

    /// <summary>HTML body, or <see langword="null"/>.</summary>
    public string? HtmlBody { get; }

    private static string ValidateAddress(string address, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(address) || !System.Net.Mail.MailAddress.TryCreate(address, out _))
        {
            throw new ArgumentException($"'{address}' is not a valid e-mail address.", parameterName);
        }

        return address.Trim();
    }

    private static IReadOnlyList<string> ValidateAddresses(IEnumerable<string> addresses, string parameterName)
    {
        var list = new List<string>();
        foreach (var address in addresses)
        {
            ArgumentNullException.ThrowIfNull(address, parameterName);
            list.Add(ValidateAddress(address, parameterName));
        }

        return list.AsReadOnly();
    }
}
