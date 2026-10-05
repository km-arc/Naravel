using Naravel.Foundation;

namespace Naravel.Queue.Options;

/// <summary>Configuration for named queue connections.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the queue configuration's default connection and connection list.
/// <para>It exists to bind queue stores to Naravel's shared manager; Laravel's PHP configuration mechanics are not ported.</para>
/// </remarks>
public sealed class QueueOptions : ManagerOptions
{
}