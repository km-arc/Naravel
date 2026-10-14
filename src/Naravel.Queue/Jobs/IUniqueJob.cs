namespace Naravel.Queue.Jobs;

/// <summary>
/// Marks a job as unique: while one dispatch of a given <see cref="UniqueKey"/> is pending or running, another dispatch of the
/// same key is rejected and <c>IJobDispatcher.DispatchAsync</c> returns <see langword="null"/> (PDR-012b).
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>ShouldBeUnique</c> / <c>ShouldBeUniqueUntilProcessing</c>.
/// Uniqueness needs an <see cref="Naravel.Queue.Dispatch.IUniqueJobGate"/>; <c>AddQueueCoordination()</c> in
/// <c>Naravel.Queue.Coordination</c> registers one. Without a gate, dispatching a unique job throws instead of silently
/// ignoring uniqueness. With a Memory cache store uniqueness holds inside one process only.
/// Unique jobs cannot be part of a chain or a batch: a rejected link would stall the chain or leave the batch incomplete.
/// </remarks>
public interface IUniqueJob : IJob
{
    /// <summary>Caller-supplied identity of "the same job" (for example <c>$"report:{ReportId}"</c>). Never derived from a type name.</summary>
    string UniqueKey { get; }

    /// <summary>
    /// Safety-net lifetime of the uniqueness lock. The lock is normally released by the worker when the job finishes or fails
    /// permanently; this bound only matters when a worker dies. Default: one hour.
    /// </summary>
    TimeSpan UniqueFor => TimeSpan.FromHours(1);

    /// <summary>
    /// <see langword="false"/> (default): the key stays locked until the job finishes or fails permanently.
    /// <see langword="true"/>: the key is released as soon as the job starts, so a new dispatch may queue while it runs.
    /// </summary>
    bool UniqueUntilProcessing => false;
}
