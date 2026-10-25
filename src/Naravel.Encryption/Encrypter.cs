using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.DataProtection;

namespace Naravel.Encryption;

internal sealed class Encrypter : IEncrypter
{
    internal const string DefaultPurpose = "Naravel.Encryption.v1";

    internal const string FailureMessage =
        "The value could not be decrypted. First check that every application and instance that reads it shares the same Data Protection key ring and application name; "
        + "then check that it is not malformed or tampered with and was written under the same purpose.";

    internal const string TypeMessage = "The value was decrypted but is not valid for the requested type.";

    private readonly IDataProtectionProvider _provider;
    private readonly string _purpose;
    private readonly IDataProtector _protector;

    public Encrypter(IDataProtectionProvider provider, string purpose)
    {
        _provider = provider;
        _purpose = purpose;
        _protector = provider.CreateProtector(purpose);
    }

    public string Encrypt(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var payload = _protector.Protect(value);
        EncryptionTelemetry.Record("encrypt", "ok");
        return payload;
    }

    public string Decrypt(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        try
        {
            var value = Unprotect(payload);
            EncryptionTelemetry.Record("decrypt", "ok");
            return value;
        }
        catch (DecryptException)
        {
            EncryptionTelemetry.Record("decrypt", "invalid");
            throw;
        }
    }

    public bool TryDecrypt(string payload, out string? value)
    {
        ArgumentNullException.ThrowIfNull(payload);
        try
        {
            value = Unprotect(payload);
            EncryptionTelemetry.Record("decrypt", "ok");
            return true;
        }
        catch (DecryptException)
        {
            EncryptionTelemetry.Record("decrypt", "invalid");
            value = null;
            return false;
        }
    }

    public string Encrypt<T>(T value) => Encrypt(JsonSerializer.Serialize(value));

    public string Encrypt<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return Encrypt(JsonSerializer.Serialize(value, typeInfo));
    }

    public T Decrypt<T>(string payload) => FromJson(payload, json => JsonSerializer.Deserialize<T>(json));

    public T Decrypt<T>(string payload, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return FromJson(payload, json => JsonSerializer.Deserialize(json, typeInfo));
    }

    public IEncrypter ForPurpose(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return new Encrypter(_provider, _purpose + "/" + purpose);
    }

    private T FromJson<T>(string payload, Func<string, T?> read)
    {
        var json = Decrypt(payload);
        try
        {
            return read(json)!;
        }
        catch (JsonException)
        {
            // Not the inner exception: a JSON error can quote a part of the plaintext.
            EncryptionTelemetry.Record("decrypt", "invalid");
            throw new DecryptException(TypeMessage);
        }
    }

    private string Unprotect(string payload)
    {
        try
        {
            return _protector.Unprotect(payload);
        }
        catch (CryptographicException exception)
        {
            throw new DecryptException(FailureMessage, exception);
        }
        catch (FormatException)
        {
            throw new DecryptException(FailureMessage);
        }
    }
}
