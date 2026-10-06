namespace Naravel.Queue.Database;

/// <summary>EF Core entity backing the persistent failed-job envelope store.</summary>
/// <remarks>Laravel stores failed jobs in a table; this entity keeps the registered alias and serialized envelope, never a CLR type name.</remarks>
public sealed class FailedJobRecord
{
    public string Id { get; set; } = default!;
    public string Envelope { get; set; } = default!;
    public string Error { get; set; } = default!;
    public DateTimeOffset FailedAt { get; set; }
}