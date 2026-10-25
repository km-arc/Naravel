namespace Naravel.Queue.Encryption;

/// <summary>Marks a queue job whose serialized payload is stored encrypted.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Contracts\Queue\ShouldBeEncrypted</c>.</para>
/// <para><b>Why it exists:</b> opt-in per job class, so nothing is encrypted by default and existing workers keep working (PDR-021). It lives in this adapter
/// package so <c>Naravel.Queue</c> does not depend on encryption. It is an empty interface: implement it on the job class.</para>
/// <para><b>Requires</b> <c>AddNaravelQueueEncryption()</c> and a Data Protection key ring and application name shared by the producer and every worker.</para>
/// <para><b>Not ported on purpose:</b> encrypting every job, and choosing the job type from the encrypted data (the alias registry alone does that, OD-07).</para>
/// </remarks>
public interface IShouldBeEncrypted
{
}
