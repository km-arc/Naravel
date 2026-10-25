using StackExchange.Redis;

namespace Naravel.Redis;

internal sealed class RedisConnection(string name, IConnectionMultiplexer multiplexer) : IRedisConnection, IDisposable
{
    private int _disposed;

    public string Name => name;

    public IConnectionMultiplexer Multiplexer => multiplexer;

    // The manager disposes every connection it created exactly once; the flag keeps a second call harmless.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            multiplexer.Dispose();
        }
    }
}
