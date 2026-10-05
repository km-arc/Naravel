using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;

namespace Naravel.Routing;

/// <summary>
/// Turns the middleware references of a route into an ordered, de-duplicated list, and caches that list per endpoint.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Router::resolveMiddleware</c>, <c>MiddlewareNameResolver</c> and <c>SortedMiddleware</c>.</para>
/// <para><b>Rules:</b> groups expand first (a group name wins over an alias with the same name); <c>name:a,b</c> becomes arguments;
/// excluded middleware are removed by identity (the type for type-based aliases, so excluding <c>"auth"</c> also removes
/// <c>"auth:web"</c>); duplicates with the same identity and arguments keep the first; then priority sorting is applied.</para>
/// <para><b>Not ported:</b> Laravel's exact priority algorithm. Here listed middleware are sorted within the slots they already occupy.</para>
/// </remarks>
public sealed class MiddlewarePipelineFactory(IOptions<RoutingOptions> options)
{
    private const int MaxGroupDepth = 16;

    private readonly MiddlewareOptions _middleware = options.Value.Middleware;
    private readonly ConditionalWeakTable<Endpoint, IReadOnlyList<ResolvedMiddleware>> _cache = new();

    /// <summary>Returns the (cached) pipeline for an endpoint, built from its metadata and controller attributes.</summary>
    public IReadOnlyList<ResolvedMiddleware> GetPipeline(Endpoint endpoint) => _cache.GetValue(endpoint, Build);

    private IReadOnlyList<ResolvedMiddleware> Build(Endpoint endpoint)
    {
        var specs = new List<MiddlewareSpec>();
        var without = new List<MiddlewareSpec>();

        foreach (var metadata in endpoint.Metadata.GetOrderedMetadata<RouteMiddlewareMetadata>())
        {
            specs.AddRange(metadata.Middleware);
            without.AddRange(metadata.Without);
        }

        var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ActionName;

        foreach (var attribute in endpoint.Metadata.GetOrderedMetadata<MiddlewareAttribute>())
        {
            if (attribute.AppliesTo(action)) specs.Add(MiddlewareSpec.Named(attribute.Name));
        }

        foreach (var attribute in endpoint.Metadata.GetOrderedMetadata<WithoutMiddlewareAttribute>())
        {
            if (attribute.AppliesTo(action)) without.Add(MiddlewareSpec.Named(attribute.Name));
        }

        return Resolve(specs, without);
    }

    /// <summary>
    /// Resolves references into the final ordered pipeline. Throws <see cref="InvalidOperationException"/> for an unknown alias or group.
    /// </summary>
    public IReadOnlyList<ResolvedMiddleware> Resolve(IEnumerable<MiddlewareSpec> specs, IEnumerable<MiddlewareSpec> without)
    {
        var resolved = new List<ResolvedMiddleware>();
        foreach (var spec in specs) Expand(spec, resolved, 0);

        var removed = new List<ResolvedMiddleware>();
        foreach (var spec in without) Expand(spec, removed, 0);
        var excluded = removed.Select(r => r.Identity).ToHashSet();

        var seen = new HashSet<(object, string)>();
        var result = new List<ResolvedMiddleware>();
        foreach (var item in resolved)
        {
            if (excluded.Contains(item.Identity)) continue;
            if (!seen.Add((item.Identity, string.Join(",", item.Arguments)))) continue;
            result.Add(item);
        }

        SortByPriority(result);
        return result;
    }

    private void Expand(MiddlewareSpec spec, List<ResolvedMiddleware> output, int depth)
    {
        if (depth > MaxGroupDepth)
        {
            throw new InvalidOperationException("Middleware groups are nested too deeply (is there a cycle?).");
        }

        if (spec.Type is not null)
        {
            output.Add(ResolvedMiddleware.ForType(spec.Type, spec.Arguments, spec.Type.Name));
            return;
        }

        if (spec.Inline is not null)
        {
            output.Add(ResolvedMiddleware.ForDelegate(spec.Inline, spec.Arguments, spec.Inline, "inline"));
            return;
        }

        var (name, arguments) = Parse(spec.Name!);

        if (_middleware.TryGetGroup(name, out var members))
        {
            foreach (var member in members) Expand(MiddlewareSpec.Named(member), output, depth + 1);
            return;
        }

        if (_middleware.TryGetAlias(name, out var target))
        {
            if (target.MiddlewareType is not null)
            {
                output.Add(ResolvedMiddleware.ForType(target.MiddlewareType, arguments, name));
            }
            else
            {
                output.Add(ResolvedMiddleware.ForDelegate(target.Inline!, arguments, name.ToLowerInvariant(), name));
            }
            return;
        }

        throw new InvalidOperationException(
            $"Middleware '{name}' is not registered. Register it with options.Middleware.Alias(...) or options.Middleware.Group(...).");
    }

    private static (string Name, string[] Arguments) Parse(string reference)
    {
        var colon = reference.IndexOf(':');
        if (colon < 0) return (reference.Trim(), Array.Empty<string>());

        var name = reference[..colon].Trim();
        var rest = reference[(colon + 1)..];
        var arguments = rest.Length == 0
            ? Array.Empty<string>()
            : rest.Split(',').Select(a => a.Trim()).ToArray();
        return (name, arguments);
    }

    private void SortByPriority(List<ResolvedMiddleware> list)
    {
        var priority = _middleware.PriorityList;
        if (priority.Count == 0) return;

        int Rank(ResolvedMiddleware item)
        {
            if (item.Type is null) return -1;
            for (var i = 0; i < priority.Count; i++)
            {
                if (priority[i] == item.Type || priority[i].IsAssignableFrom(item.Type)) return i;
            }
            return -1;
        }

        var slots = new List<int>();
        var items = new List<(ResolvedMiddleware Item, int Rank, int Order)>();
        for (var i = 0; i < list.Count; i++)
        {
            var rank = Rank(list[i]);
            if (rank < 0) continue;
            slots.Add(i);
            items.Add((list[i], rank, i));
        }

        var sorted = items.OrderBy(x => x.Rank).ThenBy(x => x.Order).ToList();
        for (var k = 0; k < slots.Count; k++) list[slots[k]] = sorted[k].Item;
    }
}
