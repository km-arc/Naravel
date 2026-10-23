namespace Naravel.Database;

/// <summary>
/// The transactions that are open for the current logical call (an <c>AsyncLocal</c> chain, one node per container and connection name).
/// It is set only for the duration of <c>TransactionAsync</c>'s callback and flows into everything the callback awaits or starts.
/// </summary>
internal static class AmbientTransaction
{
    private static readonly AsyncLocal<Node?> Current = new();

    public static string NameOf(DatabaseManager manager, string? connectionName) =>
        connectionName ?? manager.DefaultDriverName ?? "default";

    public static LeasedTransactionScope? Find(DatabaseManager manager, string name)
    {
        for (var node = Current.Value; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node.Manager, manager) && string.Equals(node.Name, name, StringComparison.Ordinal)) return node.Scope;
        }

        return null;
    }

    public static Node? Push(DatabaseManager manager, string name, LeasedTransactionScope scope)
    {
        var previous = Current.Value;
        Current.Value = new Node(manager, name, scope, previous);
        return previous;
    }

    public static void Restore(Node? previous) => Current.Value = previous;

    internal sealed record Node(DatabaseManager Manager, string Name, LeasedTransactionScope Scope, Node? Parent);
}
