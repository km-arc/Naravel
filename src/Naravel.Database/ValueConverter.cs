using System.Globalization;

namespace Naravel.Database;

/// <summary>
/// Converts one database value to the CLR type the caller asked for (<c>ValueAsync&lt;T&gt;</c>, <c>ScalarAsync&lt;T&gt;</c>, <c>PluckAsync&lt;T&gt;</c>).
/// Dapper maps rows to classes, but for a single value it hands back the raw provider value without converting it (an <c>Int64</c>
/// stays an <c>Int64</c> when <c>bool</c> is asked for, text stays text when <c>Guid</c> is asked for), and for <see cref="object"/> it returns a row object. SQLite also
/// stores <c>Guid</c>, dates and times as text. This converter closes those gaps the same way for every dialect (PDR-022 A2).
/// </summary>
internal static class ValueConverter
{
    /// <summary>True for types read as one value (numbers, text, <see cref="Guid"/>, dates, times, enums, <c>byte[]</c>, <see cref="object"/>), false for classes that Dapper maps from columns.</summary>
    public static bool IsSimple(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid) || t == typeof(DateTime)
               || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan) || t == typeof(byte[]) || t == typeof(object);
    }

    /// <summary>Converts a provider value (or <see langword="null"/> / <see cref="DBNull"/>) to <typeparamref name="T"/>; NULL becomes the default.</summary>
    public static T Convert<T>(object? value)
    {
        if (value is null or DBNull) return default!;
        if (value is T same) return same;
        return (T)ConvertTo(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static object ConvertTo(object value, Type target)
    {
        var culture = CultureInfo.InvariantCulture;
        if (target.IsInstanceOfType(value)) return value;

        if (target == typeof(Guid))
        {
            return value switch
            {
                string text => Guid.Parse(text),
                byte[] bytes when bytes.Length == 16 => new Guid(bytes),
                _ => throw Fail(value, target)
            };
        }

        if (target == typeof(DateTimeOffset))
        {
            return value switch
            {
                // A provider that returns a plain DateTime for a timezone-aware column gives UTC; an unspecified kind is read as UTC, not local time.
                DateTime date => new DateTimeOffset(date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date),
                string text => DateTimeOffset.Parse(text, culture, DateTimeStyles.AssumeUniversal),
                _ => throw Fail(value, target)
            };
        }

        if (target == typeof(DateTime))
        {
            return value switch
            {
                DateTimeOffset offset => offset.DateTime,
                string text => DateTime.Parse(text, culture, DateTimeStyles.None),
                _ => throw Fail(value, target)
            };
        }

        if (target == typeof(DateOnly))
        {
            return value switch
            {
                DateTime date => DateOnly.FromDateTime(date),
                DateTimeOffset offset => DateOnly.FromDateTime(offset.DateTime),
                string text => DateOnly.Parse(text, culture),
                _ => throw Fail(value, target)
            };
        }

        if (target == typeof(TimeOnly))
        {
            return value switch
            {
                TimeSpan span => TimeOnly.FromTimeSpan(span),
                DateTime date => TimeOnly.FromDateTime(date),
                string text => TimeOnly.Parse(text, culture),
                _ => throw Fail(value, target)
            };
        }

        if (target == typeof(TimeSpan))
        {
            return value switch
            {
                TimeOnly time => time.ToTimeSpan(),
                string text => TimeSpan.Parse(text, culture),
                _ => throw Fail(value, target)
            };
        }

        if (target.IsEnum)
        {
            return value is string name
                ? Enum.Parse(target, name, ignoreCase: true)
                : Enum.ToObject(target, System.Convert.ChangeType(value, Enum.GetUnderlyingType(target), culture));
        }

        if (target == typeof(bool))
        {
            return value is string flag
                ? flag switch { "1" => true, "0" => false, _ => bool.Parse(flag) }
                : System.Convert.ToBoolean(value, culture);
        }

        if (target == typeof(string)) return System.Convert.ToString(value, culture) ?? string.Empty;

        return System.Convert.ChangeType(value, target, culture);
    }

    private static InvalidCastException Fail(object value, Type target) =>
        new($"Cannot convert a database value of type {value.GetType().Name} to {target.Name}.");
}
