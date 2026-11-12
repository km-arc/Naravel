using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Encryption.Tests;

// Runs the "Quickstart", "Errors", "Testing" and "Complete example" sections of docs/en/encryption.md.
// The quickstart is a web app; here its registration and its calls run on a service provider, with the key ring in a folder of this test,
// so that no key is written to the profile of the machine that runs the tests.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests : IDisposable
{
    private const string Failure =
        "The value could not be decrypted. First check that every application and instance that reads it shares the same Data Protection key ring and application name; "
        + "then check that it is not malformed or tampered with and was written under the same purpose.";

    private const string WrongType = "The value was decrypted but is not valid for the requested type.";

    // The types of the complete example. IIntegrationStore is "your own data access" in the guide.
    public sealed record ApiCredentials(string Key, string Secret);

    public interface IIntegrationStore
    {
        Task SetTokenAsync(int tenantId, string token, CancellationToken ct);

        Task<string?> GetTokenAsync(int tenantId, CancellationToken ct);
    }

    private sealed class MemoryStore : IIntegrationStore
    {
        public Dictionary<int, string> Tokens { get; } = [];

        public Task SetTokenAsync(int tenantId, string token, CancellationToken ct)
        {
            Tokens[tenantId] = token;
            return Task.CompletedTask;
        }

        public Task<string?> GetTokenAsync(int tenantId, CancellationToken ct) =>
            Task.FromResult(Tokens.TryGetValue(tenantId, out var token) ? token : null);
    }

    public sealed class IntegrationService(IEncrypter encrypter, IIntegrationStore store)
    {
        private readonly IEncrypter _secrets = encrypter.ForPurpose("integrations");

        public Task SaveAsync(int tenantId, ApiCredentials credentials, CancellationToken ct) =>
            store.SetTokenAsync(tenantId, _secrets.Encrypt(credentials), ct);

        public async Task<ApiCredentials?> LoadAsync(int tenantId, CancellationToken ct)
        {
            var token = await store.GetTokenAsync(tenantId, ct);
            return token is not null && _secrets.TryDecrypt(token, out var json)
                ? JsonSerializer.Deserialize<ApiCredentials>(json!)
                : null;
        }
    }

    private readonly string _keys = Path.Combine(Path.GetTempPath(), "naravel-encryption-docs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_keys)) Directory.Delete(_keys, recursive: true);
    }

    // The helper of the "Testing" section.
    private static IEncrypter NewEncrypter(IDataProtectionProvider? keys = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(keys ?? new EphemeralDataProtectionProvider());   // without `keys`, a new key ring
        services.AddNaravelEncryption();
        return services.BuildServiceProvider().GetRequiredService<IEncrypter>();
    }

    private ServiceProvider Persistent(string application)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName(application).PersistKeysToFileSystem(new DirectoryInfo(_keys));
        services.AddNaravelEncryption();
        return services.BuildServiceProvider();
    }

    private static string Tamper(string token) => token[..^4] + (token.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

    private static void Refused<T>(Action act, string param) where T : ArgumentException =>
        act.Should().ThrowExactly<T>().Which.ParamName.Should().Be(param);

    // The invite endpoint of the complete example, without the HTTP wrapper.
    private static string Invite(string token, IEncrypter encrypter)
    {
        try
        {
            return encrypter.ForPurpose("invites").Decrypt(token);
        }
        catch (DecryptException)
        {
            return "invalid invite";
        }
    }

    [Fact]
    public void The_quickstart_seals_a_text_into_url_safe_text_and_opens_it_and_a_garbage_token_is_not_an_exception()
    {
        using var provider = Persistent("quickstart");
        var encrypter = provider.GetRequiredService<IEncrypter>();

        var token = encrypter.Encrypt("hello");                              // POST /seal

        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");                       // it goes into a URL path as it is
        encrypter.TryDecrypt(token, out var text).Should().BeTrue();         // GET /open/{token}
        text.Should().Be("hello");
        encrypter.TryDecrypt("garbage", out var none).Should().BeFalse();
        none.Should().BeNull();
    }

    [Fact]
    public void A_null_argument_names_its_parameter_and_a_blank_purpose_is_an_ArgumentException()
    {
        var encrypter = NewEncrypter();
        var card = new CardInfo("Ada", 4242);
        var token = encrypter.Encrypt("x");

        Refused<ArgumentNullException>(() => ((IServiceCollection)null!).AddNaravelEncryption(), "services");
        Refused<ArgumentNullException>(() => encrypter.Encrypt((string)null!), "value");
        Refused<ArgumentNullException>(() => encrypter.Decrypt(null!), "payload");
        Refused<ArgumentNullException>(() => encrypter.TryDecrypt(null!, out _), "payload");
        Refused<ArgumentNullException>(() => encrypter.Decrypt<CardInfo>(null!), "payload");
        Refused<ArgumentNullException>(() => encrypter.Encrypt(card, (JsonTypeInfo<CardInfo>)null!), "typeInfo");
        Refused<ArgumentNullException>(() => encrypter.Decrypt(token, (JsonTypeInfo<CardInfo>)null!), "typeInfo");
        Refused<ArgumentNullException>(() => encrypter.ForPurpose(null!), "purpose");
        Refused<ArgumentException>(() => encrypter.ForPurpose(""), "purpose");
        Refused<ArgumentException>(() => encrypter.ForPurpose(" "), "purpose");
    }

    [Fact]
    public void A_token_that_cannot_be_opened_gives_the_fixed_message_for_every_cause_and_TryDecrypt_returns_false()
    {
        var writer = NewEncrypter();
        var token = writer.Encrypt("top secret");
        string fromOtherApplication;
        using (var first = Persistent("app-a"))
        {
            fromOtherApplication = first.GetRequiredService<IEncrypter>().Encrypt("top secret");
        }

        using var second = Persistent("app-b");
        var cases = new (string Cause, IEncrypter Reader, string Token)[]
        {
            ("text that is not a token", writer, "not a token"),
            ("the empty string", writer, ""),
            ("a token that was changed", writer, Tamper(token)),
            ("another purpose", writer.ForPurpose("other"), token),
            ("another key ring", NewEncrypter(), token),
            ("another application name", second.GetRequiredService<IEncrypter>(), fromOtherApplication)
        };

        foreach (var (cause, reader, value) in cases)
        {
            var act = () => reader.Decrypt(value);

            var error = act.Should().Throw<DecryptException>(cause).Which;
            error.Message.Should().Be(Failure, cause);
            error.Message.Should().NotContain("top secret", cause);
            reader.TryDecrypt(value, out var text).Should().BeFalse(cause);
            text.Should().BeNull(cause);
        }
    }

    [Fact]
    public void The_inner_exception_is_the_data_protection_failure_and_a_type_mismatch_has_none()
    {
        var encrypter = NewEncrypter();
        var token = encrypter.Encrypt("top secret");

        var failed = ((Action)(() => encrypter.Decrypt(Tamper(token)))).Should().Throw<DecryptException>().Which;
        var mismatch = ((Action)(() => encrypter.Decrypt<CardInfo>(token))).Should().Throw<DecryptException>().Which;
        var unreadable = ((Action)(() => encrypter.Decrypt<CardInfo>("garbage"))).Should().Throw<DecryptException>().Which;

        failed.InnerException.Should().BeAssignableTo<CryptographicException>();
        mismatch.Message.Should().Be(WrongType);
        mismatch.InnerException.Should().BeNull();
        mismatch.ToString().Should().NotContain("top secret");
        unreadable.Message.Should().Be(Failure);   // the token is opened before its type is read
    }

    [Fact]
    public void The_reflection_form_and_the_source_generated_form_read_each_others_tokens()
    {
        var encrypter = NewEncrypter();
        var card = new CardInfo("Ada", 4242);

        encrypter.Decrypt(encrypter.Encrypt(card), TestJsonContext.Default.CardInfo).Should().Be(card);
        encrypter.Decrypt<CardInfo>(encrypter.Encrypt(card, TestJsonContext.Default.CardInfo)).Should().Be(card);
    }

    // The three tests of the "Testing" section, as written there.
    [Fact]
    public void A_value_round_trips_and_the_token_is_not_the_plaintext()
    {
        var encrypter = NewEncrypter();

        var token = encrypter.Encrypt("secret");

        token.Should().NotContain("secret");
        encrypter.Decrypt(token).Should().Be("secret");
    }

    [Fact]
    public void A_reader_with_another_key_ring_is_refused()
    {
        var token = NewEncrypter().Encrypt("secret");
        var other = NewEncrypter();     // another application, or the same one after it lost its keys

        other.TryDecrypt(token, out _).Should().BeFalse();
        var act = () => other.Decrypt(token);
        act.Should().Throw<DecryptException>().Which.Message.Should().Contain("key ring");
    }

    [Fact]
    public void A_reader_that_shares_the_key_ring_agrees()
    {
        var keys = new EphemeralDataProtectionProvider();

        var token = NewEncrypter(keys).Encrypt("secret");

        NewEncrypter(keys).Decrypt(token).Should().Be("secret");
    }

    [Fact]
    public async Task The_complete_example_stores_only_a_token_and_loads_the_credentials_back()
    {
        var store = new MemoryStore();
        var service = new IntegrationService(NewEncrypter(), store);
        var credentials = new ApiCredentials("key-1", "s3cret");

        await service.SaveAsync(7, credentials, CancellationToken.None);

        store.Tokens[7].Should().NotContain("s3cret").And.NotContain("key-1");
        (await service.LoadAsync(7, CancellationToken.None)).Should().Be(credentials);
        (await service.LoadAsync(8, CancellationToken.None)).Should().BeNull();   // nothing stored for the tenant
        store.Tokens[7] = Tamper(store.Tokens[7]);
        (await service.LoadAsync(7, CancellationToken.None)).Should().BeNull();   // a changed token is null, not an exception
    }

    [Fact]
    public void The_complete_example_invite_token_rejects_a_changed_or_foreign_token()
    {
        var encrypter = NewEncrypter();
        var token = encrypter.ForPurpose("invites").Encrypt("ada@example.com");   // POST /invite

        Invite(token, encrypter).Should().Be("ada@example.com");                  // GET /invite/{token}
        Invite(Tamper(token), encrypter).Should().Be("invalid invite");
        Invite(encrypter.ForPurpose("billing").Encrypt("ada@example.com"), encrypter).Should().Be("invalid invite");
        Invite("garbage", encrypter).Should().Be("invalid invite");
    }

    // The test of the "Complete example" section, as written there.
    [Fact]
    public void Invite_token_round_trips_and_purposes_are_isolated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddNaravelEncryption();
        var encrypter = services.BuildServiceProvider().GetRequiredService<IEncrypter>();

        var token = encrypter.ForPurpose("invites").Encrypt("ada@example.com");

        encrypter.ForPurpose("invites").Decrypt(token).Should().Be("ada@example.com");
        encrypter.ForPurpose("billing").TryDecrypt(token, out _).Should().BeFalse();       // another purpose cannot read it
    }
}
