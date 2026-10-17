namespace Naravel.Database;

/// <summary>Thrown by <c>SoleAsync</c> when the query matches no row.</summary>
/// <remarks><b>Laravel equivalent:</b> <c>Illuminate\Database\RecordsNotFoundException</c>.</remarks>
public sealed class RecordsNotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public RecordsNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>Thrown by <c>SoleAsync</c> when the query matches more than one row.</summary>
/// <remarks><b>Laravel equivalent:</b> <c>Illuminate\Database\MultipleRecordsFoundException</c>.</remarks>
public sealed class MultipleRecordsFoundException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public MultipleRecordsFoundException(string message) : base(message)
    {
    }
}
