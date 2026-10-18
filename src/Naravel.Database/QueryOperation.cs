namespace Naravel.Database;

/// <summary>The kind of command a query ran as. It is the only per-query detail (besides the connection name) that telemetry tags carry.</summary>
public enum QueryOperation
{
    /// <summary>A command that returns rows.</summary>
    Select = 0,

    /// <summary>A command that returns one value.</summary>
    Scalar = 1,

    /// <summary>A command that changes data or schema (insert, update, DDL).</summary>
    Statement = 2
}
