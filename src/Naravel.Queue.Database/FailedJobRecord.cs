namespace Naravel.Queue.Database;

/// <summary>EF Core entity backing the persistent failed-job envelope store.</summary>
/// <remarks>Laravel stores failed jobs in a table; this entity keeps the registered alias and serialized envelope, never a CLR type name.</remarks>
public sealed class FailedJobRecord
{
    /// <summary>Gets or sets the identifier of the failed job.</summary>
    public string Id { get; set; } = default!;
    /// <summary>Gets or sets the serialized job envelope.</summary>
    public string Envelope { get; set; } = default!;
    /// <summary>Gets or sets the error text of the failure.</summary>
    public string Error { get; set; } = default!;
    /// <summary>Gets or sets when the job failed.</summary>
    public DateTimeOffset FailedAt { get; set; }
}