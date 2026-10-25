using Naravel.Foundation;

namespace Naravel.Redis;

/// <summary>Options for <c>Naravel.Redis</c>: <c>Redis:Default</c> and <c>Redis:Stores:&lt;name&gt;:ConnectionString</c>.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the <c>redis</c> block of <c>config/database.php</c>. A store is only a <c>ConnectionString</c>: the StackExchange.Redis string already carries every
/// client option (TLS, password, <c>defaultDatabase</c>, timeouts), so Naravel invents no option names.
/// </remarks>
public sealed class RedisOptions : ManagerOptions
{
}
