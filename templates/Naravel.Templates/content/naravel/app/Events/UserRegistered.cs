namespace AppRootNamespace.App.Events;

/// <summary>
/// An application event. Keep events small (ids and scalar values): they are plain records, and a queued listener or job receives a
/// JSON copy of the payload.
/// </summary>
public sealed record UserRegistered(string Email, string Name);
