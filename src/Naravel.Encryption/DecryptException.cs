namespace Naravel.Encryption;

/// <summary>A value could not be decrypted.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Contracts\Encryption\DecryptException</c>.</para>
/// <para><b>Why it exists:</b> a tampered, malformed, wrong-purpose or key-less value must fail visibly, never return plaintext or garbage (PDR-021).
/// The message is fixed text: neither it nor the exception data ever contains the input or any plaintext.</para>
/// </remarks>
public sealed class DecryptException : Exception
{
    /// <summary>Creates the exception. The message must be fixed text that never contains an input or a plaintext.</summary>
    /// <param name="message">A fixed message.</param>
    public DecryptException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying Data Protection exception.</summary>
    /// <param name="message">A fixed message.</param>
    /// <param name="innerException">The Data Protection exception (it carries no input).</param>
    public DecryptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
