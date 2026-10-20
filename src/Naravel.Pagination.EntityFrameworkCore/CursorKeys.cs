using System.Linq.Expressions;
using System.Reflection;

namespace Naravel.Pagination;

/// <summary>Declares the keys a cursor page is ordered and filtered by, for example <c>k =&gt; k.By(p =&gt; p.CreatedAt).Then(p =&gt; p.Id)</c>.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>orderBy</c> columns that <c>cursorPaginate</c> reads from the query.</para>
/// <para><b>Why keys are declared instead of read from the query:</b> the helper then writes <b>both</b> the <c>ORDER BY</c> and the <c>WHERE</c>, so they cannot disagree, and it never has to inspect an arbitrary expression tree. Together the keys must be <b>unique</b> (end with the primary key); the helper cannot check that. A key must be a plain property or field access (<c>p =&gt; p.Id</c>, <c>p =&gt; p.Author.Id</c>), its type must have a comparison the database can run (numbers, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="TimeSpan"/>, <see cref="DateOnly"/>, <see cref="TimeOnly"/>, <see cref="string"/>; not <see cref="Guid"/>, enums or <see cref="bool"/>), and its value must not be <see langword="null"/>.</para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
public sealed class CursorKeys<T>
{
    private readonly List<CursorKey<T>> _keys = new();

    internal CursorKeys()
    {
    }

    internal IReadOnlyList<CursorKey<T>> Keys => _keys;

    /// <summary>Declares the first key. Call it once, first.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="key">A property or field access such as <c>p =&gt; p.Id</c>.</param>
    /// <param name="descending">Whether the key is ordered from high to low.</param>
    public CursorKeys<T> By<TKey>(Expression<Func<T, TKey>> key, bool descending = false)
    {
        if (_keys.Count != 0) throw new InvalidOperationException("By declares the first key; use Then for the others.");
        _keys.Add(new CursorKey<T, TKey>(key, descending));
        return this;
    }

    /// <summary>Declares a further key, used when the earlier keys are equal.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="key">A property or field access such as <c>p =&gt; p.Id</c>.</param>
    /// <param name="descending">Whether the key is ordered from high to low.</param>
    public CursorKeys<T> Then<TKey>(Expression<Func<T, TKey>> key, bool descending = false)
    {
        if (_keys.Count == 0) throw new InvalidOperationException("Declare the first key with By before Then.");
        _keys.Add(new CursorKey<T, TKey>(key, descending));
        return this;
    }
}

/// <summary>One declared key: how to order by it, compare with it and read it from an item.</summary>
internal abstract class CursorKey<T>
{
    public abstract string Name { get; }

    public abstract Type KeyType { get; }

    public abstract bool Descending { get; }

    public abstract IQueryable<T> Order(IQueryable<T> query, bool first, bool descending);

    public abstract object? Read(T item);

    public abstract Expression Equal(ParameterExpression item, object value);

    public abstract Expression Compare(ParameterExpression item, object value, bool greater);
}

internal sealed class CursorKey<T, TKey> : CursorKey<T>
{
    private readonly Expression<Func<T, TKey>> _selector;
    private readonly MemberInfo[] _path;

    public CursorKey(Expression<Func<T, TKey>> selector, bool descending)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
        Descending = descending;

        var path = new List<MemberInfo>();
        var current = selector.Body;
        while (current is MemberExpression member)
        {
            path.Insert(0, member.Member);
            current = member.Expression;
        }

        if (path.Count == 0 || current != selector.Parameters[0])
        {
            throw new ArgumentException("A cursor key must be a property or field access such as p => p.Id.", nameof(selector));
        }

        _path = path.ToArray();
        Name = string.Join('.', path.Select(member => member.Name));

        // Fail when the key is declared, not on the first request that happens to use a cursor.
        // Guid, bool and enums have no portable database ordering even where .NET offers a comparison operator (Guid has one since .NET 9).
        var underlying = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);
        if (underlying == typeof(Guid) || underlying == typeof(bool) || underlying.IsEnum)
        {
            throw new NotSupportedException($"The cursor key '{Name}' has type {typeof(TKey).Name}, which has no ordering the databases agree on. Use a number, DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly or string key (or its underlying number).");
        }

        try
        {
            _ = Compare(Expression.Parameter(typeof(T), "x"), default(TKey)!, greater: true);
        }
        catch (InvalidOperationException exception)
        {
            throw new NotSupportedException($"The cursor key '{Name}' has type {typeof(TKey).Name}, which has no comparison the database can run. Use a number, DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly or string key (or its underlying number).", exception);
        }
    }

    public override string Name { get; }

    public override Type KeyType => typeof(TKey);

    public override bool Descending { get; }

    public override IQueryable<T> Order(IQueryable<T> query, bool first, bool descending)
    {
        if (first) return descending ? Queryable.OrderByDescending(query, _selector) : Queryable.OrderBy(query, _selector);
        var ordered = (IOrderedQueryable<T>)query;
        return descending ? Queryable.ThenByDescending(ordered, _selector) : Queryable.ThenBy(ordered, _selector);
    }

    public override object? Read(T item)
    {
        object? current = item;
        foreach (var member in _path)
        {
            if (current is null) return null;
            current = member is PropertyInfo property ? property.GetValue(current) : ((FieldInfo)member).GetValue(current);
        }

        return current;
    }

    public override Expression Equal(ParameterExpression item, object value) =>
        Expression.Equal(Left(item), Right(value));

    public override Expression Compare(ParameterExpression item, object value, bool greater)
    {
        var left = Left(item);
        var right = Right(value);
        if (typeof(TKey) == typeof(string))
        {
            // EF Core translates string.Compare(a, b) > 0 to a > b, with the database's own collation on both sides of the cursor.
            var compare = Expression.Call(typeof(string).GetMethod(nameof(string.Compare), new[] { typeof(string), typeof(string) })!, left, right);
            return greater ? Expression.GreaterThan(compare, Expression.Constant(0)) : Expression.LessThan(compare, Expression.Constant(0));
        }

        return greater ? Expression.GreaterThan(left, right) : Expression.LessThan(left, right);
    }

    private Expression Left(ParameterExpression item) => new ParameterReplacer(_selector.Parameters[0], item).Visit(_selector.Body);

    // The value sits in a field of a constant holder, the shape of a captured variable, so EF Core sends it as a query parameter
    // and not as an inlined literal.
    private static Expression Right(object value) =>
        Expression.Field(Expression.Constant(new ValueBox<TKey> { Value = (TKey)value }), nameof(ValueBox<TKey>.Value));
}

internal sealed class ValueBox<TValue>
{
    public TValue Value = default!;
}

internal sealed class ParameterReplacer(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
}
