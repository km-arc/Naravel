using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Naravel.Encryption;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Encryption;

/// <summary>Decorates the registered <see cref="IJobSerializer"/>: marked jobs are written as an envelope around the inner payload.</summary>
internal sealed class EncryptedJobSerializer : IJobSerializer
{
    internal const string Purpose = "Naravel.Queue.Encryption.v1";
    internal const string Prefix = "naravel:enc:v1:";
    private const string AnyVersionPrefix = "naravel:enc:";

    internal const string FailureMessage =
        "A queued job payload could not be decrypted. First check that this worker and the application that queued the job share the same Data Protection key ring "
        + "and application name; then check that the payload was not altered and that every worker runs a version that understands the envelope.";

    internal const string UnknownVersionMessage =
        "A queued job payload uses an encryption envelope version this worker does not understand. Update the worker, or check that the producer and worker run compatible versions "
        + "and share the same Data Protection key ring and application name.";

    internal const string PlaintextRefusedMessage =
        "A job of a class marked IShouldBeEncrypted was stored as plaintext and was refused. If it was queued before the class was marked, let it drain or set "
        + "QueueEncryptionOptions.AcceptPlaintextForMarkedJobs for a deploy window; otherwise treat the message as untrusted.";

    private readonly IJobSerializer _inner;
    private readonly IDataProtector _protector;
    private readonly QueueEncryptionOptions _options;

    public EncryptedJobSerializer(IJobSerializer inner, IDataProtectionProvider provider, IOptions<QueueEncryptionOptions> options)
    {
        _inner = inner;
        _protector = provider.CreateProtector(Purpose);
        _options = options.Value;
    }

    public string GetTypeName(Type type) => _inner.GetTypeName(type);

    public Type ResolveType(string typeName) => _inner.ResolveType(typeName);

    public string Serialize(object job)
    {
        var payload = _inner.Serialize(job);
        return job is IShouldBeEncrypted ? Prefix + _protector.Protect(payload) : payload;
    }

    // Called only after the alias registry chose `type`; nothing decrypted here ever selects a type (OD-07).
    public object Deserialize(string payload, Type type)
    {
        if (payload.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return _inner.Deserialize(Decrypt(payload[Prefix.Length..]), type);
        }

        if (payload.StartsWith(AnyVersionPrefix, StringComparison.Ordinal))
        {
            throw new DecryptException(UnknownVersionMessage);
        }

        if (typeof(IShouldBeEncrypted).IsAssignableFrom(type) && !_options.AcceptPlaintextForMarkedJobs)
        {
            throw new DecryptException(PlaintextRefusedMessage);
        }

        return _inner.Deserialize(payload, type);
    }

    private string Decrypt(string protectedPayload)
    {
        try
        {
            return _protector.Unprotect(protectedPayload);
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            throw new DecryptException(FailureMessage, exception);
        }
        catch (FormatException)
        {
            throw new DecryptException(FailureMessage);
        }
    }
}
