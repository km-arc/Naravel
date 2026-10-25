using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace Naravel.Broadcasting;

/// <summary>
/// The authorization rules for <c>private-</c> and <c>presence-</c> channels.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Broadcast::channel('orders.{id}', fn ($user, $id) => ...)</c> in <c>routes/channels.php</c>.</para>
/// <para><b>Why it exists:</b> in SignalR joining a group is not authorized unless application code checks it, and forgetting the check lets any client listen to any group. Here a protected channel with no matching rule is refused.</para>
/// <para><b>Matching:</b> a pattern such as <c>private-orders.{id}</c> must match the whole channel name; a placeholder matches one or more characters except <c>.</c> (the same as Laravel). The first registered rule that matches decides.</para>
/// </remarks>
public sealed class ChannelRules
{
    private sealed record Rule(Regex Pattern, string[] Names, Func<ClaimsPrincipal, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<ChannelAuthorization>> Handler);

    private readonly object _gate = new();
    private Rule[] _rules = [];

    internal ChannelRules()
    {
    }

    /// <summary>Registers a rule for a <c>private-</c> pattern with exactly one placeholder, for example <c>private-orders.{id}</c>.</summary>
    /// <param name="pattern">The channel pattern; it must start with <c>private-</c>.</param>
    /// <param name="rule">Receives the authenticated user and the value of the placeholder.</param>
    /// <returns>The same rules, for chaining.</returns>
    public ChannelRules Authorize(string pattern, Func<ClaimsPrincipal, string, bool> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var (regex, names) = Compile(pattern, "private-");
        RequireOnePlaceholder(names);
        var name = names[0];
        return Add(new Rule(regex, names, (user, values, _) => ValueTask.FromResult(new ChannelAuthorization(rule(user, values[name]), null))));
    }

    /// <summary>Registers an asynchronous rule for a <c>private-</c> pattern that receives every placeholder by name.</summary>
    /// <param name="pattern">The channel pattern; it must start with <c>private-</c>.</param>
    /// <param name="rule">Receives the authenticated user, the placeholder values and a token that is cancelled when the connection closes.</param>
    /// <returns>The same rules, for chaining.</returns>
    public ChannelRules Authorize(
        string pattern,
        Func<ClaimsPrincipal, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<bool>> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var (regex, names) = Compile(pattern, "private-");
        return Add(new Rule(regex, names, async (user, values, ct) => new ChannelAuthorization(await rule(user, values, ct).ConfigureAwait(false), null)));
    }

    /// <summary>Registers a rule for a <c>presence-</c> pattern with exactly one placeholder. The rule returns the member for this user, or <see langword="null"/> to refuse.</summary>
    /// <param name="pattern">The channel pattern; it must start with <c>presence-</c>.</param>
    /// <param name="rule">Receives the authenticated user and the value of the placeholder.</param>
    /// <returns>The same rules, for chaining.</returns>
    public ChannelRules AuthorizePresence(string pattern, Func<ClaimsPrincipal, string, PresenceMember?> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var (regex, names) = Compile(pattern, "presence-");
        RequireOnePlaceholder(names);
        var name = names[0];
        return Add(new Rule(regex, names, (user, values, _) => ValueTask.FromResult(Presence(rule(user, values[name])))));
    }

    /// <summary>Registers an asynchronous rule for a <c>presence-</c> pattern that receives every placeholder by name and returns the member, or <see langword="null"/> to refuse.</summary>
    /// <param name="pattern">The channel pattern; it must start with <c>presence-</c>.</param>
    /// <param name="rule">Receives the authenticated user, the placeholder values and a token that is cancelled when the connection closes.</param>
    /// <returns>The same rules, for chaining.</returns>
    public ChannelRules AuthorizePresence(
        string pattern,
        Func<ClaimsPrincipal, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<PresenceMember?>> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var (regex, names) = Compile(pattern, "presence-");
        return Add(new Rule(regex, names, async (user, values, ct) => Presence(await rule(user, values, ct).ConfigureAwait(false))));
    }

    internal async ValueTask<ChannelAuthorization> AuthorizeAsync(string channel, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        foreach (var rule in _rules)
        {
            var match = rule.Pattern.Match(channel);
            if (!match.Success) continue;

            var values = new Dictionary<string, string>(rule.Names.Length, StringComparer.Ordinal);
            foreach (var name in rule.Names) values[name] = match.Groups[name].Value;
            return await rule.Handler(user, values, cancellationToken).ConfigureAwait(false);
        }

        return default;
    }

    private static ChannelAuthorization Presence(PresenceMember? member) => new(member is not null, member);

    private static void RequireOnePlaceholder(string[] names)
    {
        if (names.Length != 1)
        {
            throw new ArgumentException("This overload needs exactly one placeholder; use the overload that receives a dictionary for any other number.", "pattern");
        }
    }

    private ChannelRules Add(Rule rule)
    {
        lock (_gate) _rules = [.. _rules, rule];
        return this;
    }

    private static (Regex Regex, string[] Names) Compile(string pattern, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (!pattern.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A rule pattern for this method must start with '{prefix}'; public channels need no rule.", nameof(pattern));
        }

        var names = new List<string>();
        var builder = new StringBuilder("^");
        var literal = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '}') throw new ArgumentException("Unmatched '}' in the channel pattern.", nameof(pattern));
            if (c != '{')
            {
                literal.Append(c);
                continue;
            }

            var end = pattern.IndexOf('}', i + 1);
            if (end < 0) throw new ArgumentException("Unmatched '{' in the channel pattern.", nameof(pattern));
            var name = pattern[(i + 1)..end];
            if (!IsPlaceholderName(name) || names.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException("A placeholder must be a unique identifier such as {id}.", nameof(pattern));
            }

            builder.Append(Regex.Escape(literal.ToString()));
            literal.Clear();
            builder.Append("(?<").Append(name).Append(">[^.]+)");
            names.Add(name);
            i = end;
        }

        builder.Append(Regex.Escape(literal.ToString())).Append('$');
        var regex = new Regex(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(250));
        return (regex, names.ToArray());
    }

    private static bool IsPlaceholderName(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0])) return false;
        foreach (var c in name)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')) return false;
        }

        return true;
    }
}

internal readonly struct ChannelAuthorization(bool allowed, PresenceMember? member)
{
    public bool Allowed { get; } = allowed;

    public PresenceMember? Member { get; } = member;
}
