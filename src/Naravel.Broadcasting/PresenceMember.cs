using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Naravel.Broadcasting;

/// <summary>One member of a <c>presence-</c> channel: a user id and optional information about that user.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the array a presence channel rule returns (<c>['id' =&gt; $user-&gt;id, 'name' =&gt; $user-&gt;name]</c>).</para>
/// <para><b>Why it exists:</b> the member list is stored in the shared cache and sent to clients, so it has a small, explicit shape. Several connections of the same <see cref="UserId"/> are one member.</para>
/// </remarks>
public sealed class PresenceMember
{
    private const int MaxInfoLength = 2048;

    /// <summary>Creates a member.</summary>
    /// <param name="userId">The user id; connections with the same id are one member.</param>
    /// <param name="info">Optional information sent to clients (a name, an avatar); serialized with <c>System.Text.Json</c> and limited to 2048 characters.</param>
    [RequiresUnreferencedCode("Serialises the information with reflection-based System.Text.Json. When trimming or publishing with Native AOT pass a JsonElement (for example from JsonSerializer.SerializeToElement with a source-generated JsonTypeInfo).")]
    [RequiresDynamicCode("Serialises the information with reflection-based System.Text.Json. When publishing with Native AOT pass a JsonElement.")]
    public PresenceMember(string userId, object? info = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        UserId = userId;
        if (info is null) return;

        var element = JsonSerializer.SerializeToElement(info);
        if (element.GetRawText().Length > MaxInfoLength)
        {
            throw new ArgumentException($"The member information is larger than {MaxInfoLength} characters.", nameof(info));
        }

        Info = element;
    }

    /// <summary>Creates a member with information that is already JSON (no reflection; the safe choice for trimming and Native AOT).</summary>
    /// <param name="userId">The user id; connections with the same id are one member.</param>
    /// <param name="info">The member information; its JSON text may not exceed 2048 characters.</param>
    /// <returns>The member.</returns>
    /// <exception cref="ArgumentException">The user id is blank or the information is too large.</exception>
    public static PresenceMember FromJson(string userId, JsonElement info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (info.GetRawText().Length > MaxInfoLength)
        {
            throw new ArgumentException($"The member information is larger than {MaxInfoLength} characters.", nameof(info));
        }

        return new PresenceMember(userId, (JsonElement?)info.Clone());
    }

    private PresenceMember(string userId, JsonElement? info)
    {
        UserId = userId;
        Info = info;
    }

    /// <summary>The user id.</summary>
    public string UserId { get; }

    /// <summary>The information given when the member was created, or <see langword="null"/>.</summary>
    public JsonElement? Info { get; }

    internal static PresenceMember FromStored(string userId, string? infoJson)
    {
        if (infoJson is null) return new PresenceMember(userId, (JsonElement?)null);
        using var document = JsonDocument.Parse(infoJson);
        return new PresenceMember(userId, (JsonElement?)document.RootElement.Clone());
    }
}
