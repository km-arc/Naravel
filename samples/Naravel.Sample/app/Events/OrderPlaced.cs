namespace Naravel.Sample.App.Events;

/// <summary>
/// An application event. Events are plain serializable types: keep them small (ids and scalar values) because the
/// queued listener receives a JSON copy of this payload in the worker.
/// </summary>
public sealed record OrderPlaced(string OrderId, string Email, decimal Total);
