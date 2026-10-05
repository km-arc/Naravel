using System.Reflection;
using System.Threading.Tasks;

namespace Naravel.Cache.Tests;

public class CacheProviderDispatchProxy : DispatchProxy
{
    public bool IsDisposed { get; private set; }
    public object? NextResult { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "GetDatabase" && NextResult is not null) return NextResult;
        if (targetMethod?.Name == nameof(IDisposable.Dispose))
        {
            IsDisposed = true;
            return null;
        }

        if (targetMethod?.ReturnType == typeof(Task)) return Task.CompletedTask;
        if (targetMethod?.ReturnType == typeof(Task<bool>)) return Task.FromResult(NextResult is bool result && result);
        if (targetMethod?.ReturnType == typeof(Task<ulong>)) return Task.FromResult(NextResult is ulong result ? result : 0UL);
        if (targetMethod?.ReturnType == typeof(Task<string>)) return Task.FromResult(NextResult as string);
        if (targetMethod?.ReturnType == typeof(Task<string?>)) return Task.FromResult(NextResult as string);
        if (NextResult is not null && targetMethod?.ReturnType.IsInstanceOfType(NextResult) == true) return NextResult;
        throw new NotSupportedException($"Unexpected Memcached client call: {targetMethod?.Name}");
    }
}
