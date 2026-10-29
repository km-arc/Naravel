using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace Naravel.Encryption;

/// <summary>Encrypts and decrypts values with ASP.NET Core Data Protection.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Encryption\Encrypter</c> and the <c>Crypt</c> facade (<c>encryptString</c>, <c>decryptString</c>, <c>encrypt</c>, <c>decrypt</c>).</para>
/// <para><b>Why it exists:</b> one small, consistently named service over <c>IDataProtector</c> (<c>Encrypt</c> is <c>Protect</c>, <c>Decrypt</c> is <c>Unprotect</c>),
/// so applications and Naravel modules share one way to protect a value. Every byte of protection comes from Data Protection; Naravel adds no cryptography (OD-03, PDR-021).</para>
/// <para><b>Key ring:</b> this service never touches keys. Key persistence, protection at rest, rotation and the application name are your <c>AddDataProtection()</c>
/// configuration. Every application or instance that must read a value has to share the key ring and the application name.</para>
/// <para><b>Synchronous on purpose:</b> the Data Protection primitive is synchronous, so no async wrapper is invented.</para>
/// <para><b>Not ported on purpose:</b> Laravel-compatible ciphertext, a static <c>Crypt::</c> facade, and time-limited protection.</para>
/// </remarks>
public interface IEncrypter
{
    /// <summary>Encrypts a string. The result is URL-safe text that only a holder of the same key ring and purpose can read.</summary>
    /// <param name="value">The text to protect.</param>
    string Encrypt(string value);

    /// <summary>Decrypts text produced by <see cref="Encrypt(string)"/> under the same purpose.</summary>
    /// <param name="payload">The protected text.</param>
    /// <exception cref="DecryptException">The value is malformed, tampered with, protected for another purpose, or its key is not in the key ring.</exception>
    string Decrypt(string payload);

    /// <summary>Decrypts text and returns <see langword="false"/> instead of throwing when it cannot be decrypted.</summary>
    /// <param name="payload">The protected text.</param>
    /// <param name="value">The plain text, or <see langword="null"/> when this returns <see langword="false"/>.</param>
    bool TryDecrypt(string payload, out string? value);

    /// <summary>Encrypts a value as JSON (<c>System.Text.Json</c>). Laravel: <c>Crypt::encrypt</c>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to protect.</param>
    [RequiresUnreferencedCode("Reflection-based JSON serialization; use the overload that takes a JsonTypeInfo from a source-generated JsonSerializerContext when trimming or publishing with Native AOT.")]
    [RequiresDynamicCode("Reflection-based JSON serialization; use the overload that takes a JsonTypeInfo from a source-generated JsonSerializerContext when publishing with Native AOT.")]
    string Encrypt<T>(T value);

    /// <summary>Encrypts a value as JSON with source-generated metadata, for trimming and AOT.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to protect.</param>
    /// <param name="typeInfo">The JSON metadata for <typeparamref name="T"/>.</param>
    string Encrypt<T>(T value, JsonTypeInfo<T> typeInfo);

    /// <summary>Decrypts a value written by <c>Encrypt&lt;T&gt;</c>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="payload">The protected text.</param>
    /// <exception cref="DecryptException">The value cannot be decrypted, or it decrypts but is not valid JSON for <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode("Reflection-based JSON serialization; use the overload that takes a JsonTypeInfo from a source-generated JsonSerializerContext when trimming or publishing with Native AOT.")]
    [RequiresDynamicCode("Reflection-based JSON serialization; use the overload that takes a JsonTypeInfo from a source-generated JsonSerializerContext when publishing with Native AOT.")]
    T Decrypt<T>(string payload);

    /// <summary>Decrypts a value written by <c>Encrypt&lt;T&gt;</c> with source-generated metadata.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="payload">The protected text.</param>
    /// <param name="typeInfo">The JSON metadata for <typeparamref name="T"/>.</param>
    /// <exception cref="DecryptException">The value cannot be decrypted, or it decrypts but is not valid JSON for <typeparamref name="T"/>.</exception>
    T Decrypt<T>(string payload, JsonTypeInfo<T> typeInfo);

    /// <summary>
    /// Returns an encrypter isolated under a sub-purpose: it cannot read what this encrypter or another sub-purpose wrote, and the reverse.
    /// The default purpose is <c>Naravel.Encryption.v1</c>; <c>ForPurpose("billing")</c> uses <c>Naravel.Encryption.v1/billing</c>.
    /// </summary>
    /// <param name="purpose">A fixed, application-chosen name. Never build it from user input.</param>
    IEncrypter ForPurpose(string purpose);
}
