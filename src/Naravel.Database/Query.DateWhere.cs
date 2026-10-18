using System.Globalization;

namespace Naravel.Database;

public sealed partial class Query
{
    private static readonly string[] ComparisonOperators = ["=", "<", ">", "<=", ">=", "<>", "!="];

    /// <summary>Compares the calendar date of the column with <c>=</c>; the value is DateOnly, DateTime, DateTimeOffset or a <c>yyyy-MM-dd</c> string. Laravel: <c>whereDate</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereDate(string column, object value) => AddDatePart(DatePartKind.Date, column, "=", value, or: false);

    /// <summary>Compares the calendar date of the column with an operator; the value is DateOnly, DateTime, DateTimeOffset or a <c>yyyy-MM-dd</c> string. Laravel: <c>whereDate</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereDate(string column, string op, object value) => AddDatePart(DatePartKind.Date, column, op, value, or: false);

    /// <summary>Compares the calendar date of the column with <c>=</c> joined with <c>or</c>; the value is DateOnly, DateTime, DateTimeOffset or a <c>yyyy-MM-dd</c> string. Laravel: <c>orWhereDate</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereDate(string column, object value) => AddDatePart(DatePartKind.Date, column, "=", value, or: true);

    /// <summary>Compares the calendar date of the column with an operator joined with <c>or</c>; the value is DateOnly, DateTime, DateTimeOffset or a <c>yyyy-MM-dd</c> string. Laravel: <c>orWhereDate</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereDate(string column, string op, object value) => AddDatePart(DatePartKind.Date, column, op, value, or: true);

    /// <summary>Compares the month number of the column with <c>=</c>; the value is a month from 1 to 12. Laravel: <c>whereMonth</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereMonth(string column, int value) => AddDatePart(DatePartKind.Month, column, "=", value, or: false);

    /// <summary>Compares the month number of the column with an operator; the value is a month from 1 to 12. Laravel: <c>whereMonth</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereMonth(string column, string op, int value) => AddDatePart(DatePartKind.Month, column, op, value, or: false);

    /// <summary>Compares the month number of the column with <c>=</c> joined with <c>or</c>; the value is a month from 1 to 12. Laravel: <c>orWhereMonth</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereMonth(string column, int value) => AddDatePart(DatePartKind.Month, column, "=", value, or: true);

    /// <summary>Compares the month number of the column with an operator joined with <c>or</c>; the value is a month from 1 to 12. Laravel: <c>orWhereMonth</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereMonth(string column, string op, int value) => AddDatePart(DatePartKind.Month, column, op, value, or: true);

    /// <summary>Compares the day of the month of the column with <c>=</c>; the value is a day of the month from 1 to 31. Laravel: <c>whereDay</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereDay(string column, int value) => AddDatePart(DatePartKind.Day, column, "=", value, or: false);

    /// <summary>Compares the day of the month of the column with an operator; the value is a day of the month from 1 to 31. Laravel: <c>whereDay</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereDay(string column, string op, int value) => AddDatePart(DatePartKind.Day, column, op, value, or: false);

    /// <summary>Compares the day of the month of the column with <c>=</c> joined with <c>or</c>; the value is a day of the month from 1 to 31. Laravel: <c>orWhereDay</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereDay(string column, int value) => AddDatePart(DatePartKind.Day, column, "=", value, or: true);

    /// <summary>Compares the day of the month of the column with an operator joined with <c>or</c>; the value is a day of the month from 1 to 31. Laravel: <c>orWhereDay</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereDay(string column, string op, int value) => AddDatePart(DatePartKind.Day, column, op, value, or: true);

    /// <summary>Compares the year of the column with <c>=</c>; the value is a year from 1 to 9999. Laravel: <c>whereYear</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereYear(string column, int value) => AddDatePart(DatePartKind.Year, column, "=", value, or: false);

    /// <summary>Compares the year of the column with an operator; the value is a year from 1 to 9999. Laravel: <c>whereYear</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereYear(string column, string op, int value) => AddDatePart(DatePartKind.Year, column, op, value, or: false);

    /// <summary>Compares the year of the column with <c>=</c> joined with <c>or</c>; the value is a year from 1 to 9999. Laravel: <c>orWhereYear</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereYear(string column, int value) => AddDatePart(DatePartKind.Year, column, "=", value, or: true);

    /// <summary>Compares the year of the column with an operator joined with <c>or</c>; the value is a year from 1 to 9999. Laravel: <c>orWhereYear</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereYear(string column, string op, int value) => AddDatePart(DatePartKind.Year, column, op, value, or: true);

    /// <summary>Compares the time of day of the column with <c>=</c>; the value is TimeOnly, TimeSpan, DateTime, DateTimeOffset or an <c>HH:mm[:ss]</c> string. Laravel: <c>whereTime</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereTime(string column, object value) => AddDatePart(DatePartKind.Time, column, "=", value, or: false);

    /// <summary>Compares the time of day of the column with an operator; the value is TimeOnly, TimeSpan, DateTime, DateTimeOffset or an <c>HH:mm[:ss]</c> string. Laravel: <c>whereTime</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query WhereTime(string column, string op, object value) => AddDatePart(DatePartKind.Time, column, op, value, or: false);

    /// <summary>Compares the time of day of the column with <c>=</c> joined with <c>or</c>; the value is TimeOnly, TimeSpan, DateTime, DateTimeOffset or an <c>HH:mm[:ss]</c> string. Laravel: <c>orWhereTime</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereTime(string column, object value) => AddDatePart(DatePartKind.Time, column, "=", value, or: true);

    /// <summary>Compares the time of day of the column with an operator joined with <c>or</c>; the value is TimeOnly, TimeSpan, DateTime, DateTimeOffset or an <c>HH:mm[:ss]</c> string. Laravel: <c>orWhereTime</c>.</summary>
    /// <param name="column">The date or time column.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The value to compare with.</param>
    public Query OrWhereTime(string column, string op, object value) => AddDatePart(DatePartKind.Time, column, op, value, or: true);

    /// <summary>Compares each column with the value with <c>=</c>; the condition holds when at least one of the columns matches (joined with <c>or</c> inside parentheses). Every column gets its own parameter. Laravel: <c>whereAny</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query WhereAny(IEnumerable<string> columns, object? value) => AddMulti(MultiMode.Any, columns, "=", value, or: false);

    /// <summary>Compares each column with the value using an operator; the condition holds when at least one of the columns matches (joined with <c>or</c> inside parentheses). Every column gets its own parameter. Laravel: <c>whereAny</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query WhereAny(IEnumerable<string> columns, string op, object? value) => AddMulti(MultiMode.Any, columns, op, value, or: false);

    /// <summary>Compares each column with the value with <c>=</c>; the condition holds when at least one of the columns matches (joined with <c>or</c> inside parentheses) joined with <c>or</c>. Every column gets its own parameter. Laravel: <c>orWhereAny</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query OrWhereAny(IEnumerable<string> columns, object? value) => AddMulti(MultiMode.Any, columns, "=", value, or: true);

    /// <summary>Compares each column with the value using an operator; the condition holds when at least one of the columns matches (joined with <c>or</c> inside parentheses) joined with <c>or</c>. Every column gets its own parameter. Laravel: <c>orWhereAny</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query OrWhereAny(IEnumerable<string> columns, string op, object? value) => AddMulti(MultiMode.Any, columns, op, value, or: true);

    /// <summary>Compares each column with the value with <c>=</c>; the condition holds when every column matches (joined with <c>and</c> inside parentheses). Every column gets its own parameter. Laravel: <c>whereAll</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query WhereAll(IEnumerable<string> columns, object? value) => AddMulti(MultiMode.All, columns, "=", value, or: false);

    /// <summary>Compares each column with the value using an operator; the condition holds when every column matches (joined with <c>and</c> inside parentheses). Every column gets its own parameter. Laravel: <c>whereAll</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query WhereAll(IEnumerable<string> columns, string op, object? value) => AddMulti(MultiMode.All, columns, op, value, or: false);

    /// <summary>Compares each column with the value with <c>=</c>; the condition holds when every column matches (joined with <c>and</c> inside parentheses) joined with <c>or</c>. Every column gets its own parameter. Laravel: <c>orWhereAll</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query OrWhereAll(IEnumerable<string> columns, object? value) => AddMulti(MultiMode.All, columns, "=", value, or: true);

    /// <summary>Compares each column with the value using an operator; the condition holds when every column matches (joined with <c>and</c> inside parentheses) joined with <c>or</c>. Every column gets its own parameter. Laravel: <c>orWhereAll</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query OrWhereAll(IEnumerable<string> columns, string op, object? value) => AddMulti(MultiMode.All, columns, op, value, or: true);

    /// <summary>Compares each column with the value with <c>=</c>; the condition holds when no column matches (<c>not</c> of the <c>or</c> group). Every column gets its own parameter. Laravel: <c>whereNone</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query WhereNone(IEnumerable<string> columns, object? value) => AddMulti(MultiMode.None, columns, "=", value, or: false);

    /// <summary>Compares each column with the value using an operator; the condition holds when no column matches (<c>not</c> of the <c>or</c> group). Every column gets its own parameter. Laravel: <c>whereNone</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query WhereNone(IEnumerable<string> columns, string op, object? value) => AddMulti(MultiMode.None, columns, op, value, or: false);

    /// <summary>Compares each column with the value with <c>=</c>; the condition holds when no column matches (<c>not</c> of the <c>or</c> group) joined with <c>or</c>. Every column gets its own parameter. Laravel: <c>orWhereNone</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query OrWhereNone(IEnumerable<string> columns, object? value) => AddMulti(MultiMode.None, columns, "=", value, or: true);

    /// <summary>Compares each column with the value using an operator; the condition holds when no column matches (<c>not</c> of the <c>or</c> group) joined with <c>or</c>. Every column gets its own parameter. Laravel: <c>orWhereNone</c>.</summary>
    /// <param name="columns">The columns, at least one.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, bound once per column.</param>
    public Query OrWhereNone(IEnumerable<string> columns, string op, object? value) => AddMulti(MultiMode.None, columns, op, value, or: true);

    private enum MultiMode
    {
        Any,
        All,
        None,
    }

    private Query AddDatePart(DatePartKind kind, string column, string op, object value, bool or)
    {
        var identifier = Column(column, nameof(column));
        ArgumentNullException.ThrowIfNull(op);
        var normalized = Grammar.NormalizeOperator(op, nameof(op));
        if (Array.IndexOf(ComparisonOperators, normalized) < 0)
        {
            throw new ArgumentException($"'{normalized}' cannot compare a date or time part; use one of = < > <= >= <> !=.", nameof(op));
        }

        _wheres.Add(new DateWhere(kind, identifier, normalized, DatePartValue(kind, value), or));
        return this;
    }

    private static object DatePartValue(DatePartKind kind, object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        switch (kind)
        {
            case DatePartKind.Month: return Ranged((int)value, 1, 12, "month");
            case DatePartKind.Day: return Ranged((int)value, 1, 31, "day");
            case DatePartKind.Year: return Ranged((int)value, 1, 9999, "year");
            case DatePartKind.Date:
                return value switch
                {
                    DateOnly date => date,
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    DateTimeOffset offset => DateOnly.FromDateTime(offset.DateTime),
                    string text when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
                    _ => throw new ArgumentException("A date must be a DateOnly, DateTime, DateTimeOffset or a 'yyyy-MM-dd' string.", nameof(value)),
                };
            default:
                return value switch
                {
                    TimeOnly time => time,
                    TimeSpan span when span >= TimeSpan.Zero && span < TimeSpan.FromDays(1) => TimeOnly.FromTimeSpan(span),
                    DateTime dateTime => TimeOnly.FromDateTime(dateTime),
                    DateTimeOffset offset => TimeOnly.FromDateTime(offset.DateTime),
                    string text when TimeOnly.TryParseExact(text, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
                    _ => throw new ArgumentException("A time must be a TimeOnly, a TimeSpan inside one day, a DateTime, a DateTimeOffset or an 'HH:mm[:ss]' string.", nameof(value)),
                };
        }
    }

    private static int Ranged(int value, int min, int max, string what) =>
        value >= min && value <= max ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"A {what} must be from {min} to {max}.");

    private Query AddMulti(MultiMode mode, IEnumerable<string> columns, string op, object? value, bool or)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var inner = new Query(Grammar);
        foreach (var column in columns) inner.AddBasic(column, op, value, or: mode != MultiMode.All && inner._wheres.Count > 0, not: false);
        if (inner._wheres.Count == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        _wheres.Add(new NestedWhere(inner._wheres.ToArray(), or, mode == MultiMode.None));
        return this;
    }
}
