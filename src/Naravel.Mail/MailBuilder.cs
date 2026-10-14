namespace Naravel.Mail;

/// <summary>
/// Collects the parts of a <see cref="MailMessage"/> for a <see cref="Mailable"/>.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the fluent <c>from()/subject()/view()</c> calls inside <c>build()</c>.
/// <b>Why it exists:</b> readable mailables without a long constructor call. <b>Not ported:</b> attachments, views, markdown.
/// </remarks>
public sealed class MailBuilder
{
    private readonly List<string> _to;
    private readonly List<string> _cc = new();
    private readonly List<string> _bcc = new();
    private string? _from;
    private string _subject = string.Empty;
    private string? _text;
    private string? _html;

    /// <summary>Creates a builder.</summary>
    /// <param name="to">The recipients to start with.</param>
    /// <param name="defaultFrom">The sender used until <see cref="From"/> is called, or <see langword="null"/>.</param>
    public MailBuilder(IEnumerable<string> to, string? defaultFrom = null)
    {
        ArgumentNullException.ThrowIfNull(to);
        _to = new List<string>(to);
        _from = defaultFrom;
    }

    /// <summary>Sets the sender.</summary>
    /// <param name="address">Sender address.</param>
    public MailBuilder From(string address)
    {
        _from = address;
        return this;
    }

    /// <summary>Adds a recipient.</summary>
    /// <param name="address">Recipient address.</param>
    public MailBuilder To(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _to.Add(address);
        return this;
    }

    /// <summary>Adds a carbon-copy recipient.</summary>
    /// <param name="address">Cc address.</param>
    public MailBuilder Cc(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _cc.Add(address);
        return this;
    }

    /// <summary>Adds a blind carbon-copy recipient.</summary>
    /// <param name="address">Bcc address.</param>
    public MailBuilder Bcc(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _bcc.Add(address);
        return this;
    }

    /// <summary>Sets the subject.</summary>
    /// <param name="subject">Subject line.</param>
    public MailBuilder Subject(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the plain-text body.</summary>
    /// <param name="text">Body text.</param>
    public MailBuilder Text(string text)
    {
        _text = text;
        return this;
    }

    /// <summary>Sets the HTML body.</summary>
    /// <param name="html">Body HTML.</param>
    public MailBuilder Html(string html)
    {
        _html = html;
        return this;
    }

    /// <summary>Validates the parts and creates the message.</summary>
    /// <exception cref="InvalidOperationException">No sender was set and <c>Mail:From</c> is not configured.</exception>
    /// <exception cref="ArgumentException">An address is invalid, or recipients or body are missing.</exception>
    public MailMessage Build()
    {
        if (string.IsNullOrWhiteSpace(_from))
        {
            throw new InvalidOperationException(
                "The mail has no sender. Call From(...) in Mailable.Build or set Mail:From in configuration.");
        }

        return new MailMessage(_from, _to, _subject, _text, _html, _cc, _bcc);
    }
}
