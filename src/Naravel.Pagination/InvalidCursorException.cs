namespace Naravel.Pagination;

/// <summary>A cursor from a client could not be read: it is too long, not valid base64url, not the expected JSON, or its values do not fit the declared keys.</summary>
/// <remarks>An application normally maps this to HTTP 400. The message never contains the cursor, so it is safe to log.</remarks>
public sealed class InvalidCursorException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, without the cursor text.</param>
    public InvalidCursorException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, without the cursor text.</param>
    /// <param name="innerException">The parse failure.</param>
    public InvalidCursorException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
