namespace Naravel.Broadcasting;

/// <summary>
/// An event that knows which channels it is delivered to.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>ShouldBroadcast</c> with <c>broadcastOn()</c>, <c>broadcastAs()</c> and <c>broadcastWith()</c>.</para>
/// <para><b>Why it exists:</b> SignalR group names and payload shapes are free strings and objects scattered over call sites; this puts the contract on the event.</para>
/// <para><b>Not ported on purpose:</b> automatic broadcasting of every implementing event. <c>Naravel.Events</c> dispatches by exact type, so each event is opted in with <c>AddBroadcastOn&lt;TEvent&gt;()</c>.</para>
/// </remarks>
public interface IBroadcastable
{
    /// <summary>The channels the event is sent to. Names use letters, digits and <c>_ . : -</c>, at most 128 characters; a name that starts with <c>private-</c> or <c>presence-</c> needs an authorization rule to be subscribed to.</summary>
    IEnumerable<string> Channels();

    /// <summary>The client-side method name. Defaults to the type name.</summary>
    string BroadcastAs() => GetType().Name;

    /// <summary>The payload sent to clients, serialized by SignalR with <c>System.Text.Json</c>. Defaults to the event itself.</summary>
    object BroadcastWith() => this;
}
