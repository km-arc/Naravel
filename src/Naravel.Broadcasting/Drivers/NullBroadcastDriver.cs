namespace Naravel.Broadcasting.Drivers;

/// <summary>Discards every message.</summary>
internal sealed class NullBroadcastDriver : IBroadcastDriver
{
    public Task SendAsync(BroadcastMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
