namespace Naravel.Mail.Smtp;

/// <summary>How the connection to the SMTP server is secured.</summary>
/// <remarks>The default for <see cref="SmtpMailOptions.Security"/> is <see cref="StartTls"/>: TLS is required, never optional.</remarks>
public enum SmtpSecurity
{
    /// <summary>Upgrade the connection with STARTTLS and fail if the server does not offer it (typically port 587).</summary>
    StartTls = 0,

    /// <summary>TLS from the first byte (implicit TLS, typically port 465).</summary>
    SslOnConnect = 1,

    /// <summary>No encryption. Credentials and message travel in clear text; only for a local test server.</summary>
    None = 2
}

/// <summary>Settings of one SMTP transport, read from <c>Mail:Mailers:&lt;name&gt;</c>.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a <c>smtp</c> entry in <c>config/mail.php</c> (<c>host</c>, <c>port</c>, <c>username</c>,
/// <c>password</c>, <c>encryption</c>, <c>timeout</c>).</para>
/// <para>Keep <see cref="Password"/> out of source control: use user secrets or environment variables
/// (<c>Mail__Mailers__smtp__Password</c>).</para>
/// </remarks>
public sealed class SmtpMailOptions
{
    /// <summary>SMTP server host name. Required.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP server port. Defaults to 587 (STARTTLS).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Connection security. Defaults to <see cref="SmtpSecurity.StartTls"/>.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>User name, or <see langword="null"/> to send without authentication.</summary>
    public string? Username { get; set; }

    /// <summary>Password for <see cref="Username"/>.</summary>
    public string? Password { get; set; }

    /// <summary>Network timeout for each SMTP operation. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException("The smtp mail transport needs a Host (Mail:Mailers:<name>:Host).");
        }

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"The smtp mail transport Port {Port} is not between 1 and 65535.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The smtp mail transport Timeout must be greater than zero.");
        }
    }
}
