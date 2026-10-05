namespace Naravel.Routing;

/// <summary>
/// The middleware registry: aliases, groups and priority. Configured once through
/// <c>AddNaravelRouting(o =&gt; o.Middleware...)</c>.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>$middlewareAliases</c>, <c>$middlewareGroups</c>, <c>$middlewarePriority</c> and the
/// <c>Middleware</c> configuration object (<c>alias()</c>, <c>appendToGroup()</c>, <c>prependToGroup()</c>, <c>replace()</c>, <c>remove()</c>).</para>
/// <para><b>Why it exists:</b> ASP.NET Core has no named, groupable, per-route middleware registry.</para>
/// <para><b>Not ported:</b> resolving a middleware by fully-qualified class name string; register an alias or pass the type.
/// Configuration is code-time and read once (it is not hot-reloaded, unlike driver modules).</para>
/// </remarks>
public sealed class MiddlewareOptions
{
    internal sealed record Target(Type? MiddlewareType, RouteMiddlewareDelegate? Inline);

    private readonly Dictionary<string, Target> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Type> _priority = new();

    /// <summary>Registers (or replaces) an alias for a middleware type.</summary>
    public MiddlewareOptions Alias(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        MiddlewareTypes.Validate(type);
        _aliases[name] = new Target(type, null);
        return this;
    }

    /// <summary>Registers (or replaces) an alias for a middleware type.</summary>
    public MiddlewareOptions Alias<T>(string name) where T : class => Alias(name, typeof(T));

    /// <summary>Registers (or replaces) an alias for an inline middleware.</summary>
    public MiddlewareOptions Alias(string name, RouteMiddlewareDelegate middleware)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(middleware);
        _aliases[name] = new Target(null, middleware);
        return this;
    }

    /// <summary>Defines (or replaces) a group. Members are alias names (with optional arguments) or other group names.</summary>
    public MiddlewareOptions Group(string name, params string[] members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _groups[name] = new List<string>(members);
        return this;
    }

    /// <summary>Adds a member at the start of a group (the group is created when missing).</summary>
    public MiddlewareOptions PrependToGroup(string group, string member)
    {
        GetOrCreate(group).Insert(0, member);
        return this;
    }

    /// <summary>Adds a member at the end of a group (the group is created when missing).</summary>
    public MiddlewareOptions AppendToGroup(string group, string member)
    {
        GetOrCreate(group).Add(member);
        return this;
    }

    /// <summary>Replaces a member of an existing group; does nothing if the group or member does not exist.</summary>
    public MiddlewareOptions ReplaceInGroup(string group, string search, string replace)
    {
        if (_groups.TryGetValue(group, out var members))
        {
            var i = members.IndexOf(search);
            if (i >= 0) members[i] = replace;
        }
        return this;
    }

    /// <summary>Removes a member from an existing group; does nothing if the group or member does not exist.</summary>
    public MiddlewareOptions RemoveFromGroup(string group, string member)
    {
        if (_groups.TryGetValue(group, out var members)) members.Remove(member);
        return this;
    }

    /// <summary>
    /// Sets the execution priority. Middleware whose type is listed is re-ordered to follow this list; each one is placed in a slot
    /// that a listed middleware already occupied, so unlisted middleware keep their positions.
    /// </summary>
    public MiddlewareOptions Priority(params Type[] types)
    {
        _priority.Clear();
        _priority.AddRange(types);
        return this;
    }

    private List<string> GetOrCreate(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        if (!_groups.TryGetValue(group, out var members)) _groups[group] = members = new List<string>();
        return members;
    }

    internal bool TryGetGroup(string name, out IReadOnlyList<string> members)
    {
        if (_groups.TryGetValue(name, out var list))
        {
            members = list.ToArray();
            return true;
        }
        members = Array.Empty<string>();
        return false;
    }

    internal bool TryGetAlias(string name, out Target target) => _aliases.TryGetValue(name, out target!);

    internal IReadOnlyList<Type> PriorityList => _priority;
}
