using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Encryption.Tests;

public sealed record CardInfo(string Holder, int Last4);

[JsonSerializable(typeof(CardInfo))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

public sealed class EncrypterTests : IDisposable
{
    private readonly string _keys = Path.Combine(Path.GetTempPath(), "naravel-encryption-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_keys)) Directory.Delete(_keys, recursive: true);
    }

    private static IEncrypter Ephemeral()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddNaravelEncryption();
        return services.BuildServiceProvider().GetRequiredService<IEncrypter>();
    }

    private ServiceProvider Persistent(string application = "app")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName(application).PersistKeysToFileSystem(new DirectoryInfo(_keys));
        services.AddNaravelEncryption();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_string_round_trips_and_the_token_is_not_the_plaintext()
    {
        var encrypter = Ephemeral();

        var token = encrypter.Encrypt("top secret");

        token.Should().NotContain("top secret");
        encrypter.Decrypt(token).Should().Be("top secret");
    }

    [Fact]
    public void An_empty_string_round_trips()
    {
        var encrypter = Ephemeral();

        encrypter.Decrypt(encrypter.Encrypt(string.Empty)).Should().BeEmpty();
    }

    [Fact]
    public void A_typed_value_round_trips()
    {
        var encrypter = Ephemeral();

        var card = encrypter.Decrypt<CardInfo>(encrypter.Encrypt(new CardInfo("Ada", 4242)));

        card.Should().Be(new CardInfo("Ada", 4242));
    }

    [Fact]
    public void A_typed_value_round_trips_with_source_generated_metadata()
    {
        var encrypter = Ephemeral();

        var token = encrypter.Encrypt(new CardInfo("Ada", 4242), TestJsonContext.Default.CardInfo);

        encrypter.Decrypt(token, TestJsonContext.Default.CardInfo).Should().Be(new CardInfo("Ada", 4242));
    }

    [Fact]
    public void A_tampered_token_is_refused_and_the_message_never_contains_the_input()
    {
        var encrypter = Ephemeral();
        var token = encrypter.Encrypt("top secret");
        var tampered = token[..^4] + (token.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var act = () => encrypter.Decrypt(tampered);

        var thrown = act.Should().Throw<DecryptException>().Which;
        thrown.Message.Should().NotContain(tampered).And.NotContain("top secret").And.Contain("key ring");
        thrown.ToString().Should().NotContain("top secret");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("%%%")]
    public void A_malformed_token_is_refused_without_echoing_it(string garbage)
    {
        var encrypter = Ephemeral();

        var act = () => encrypter.Decrypt(garbage);

        var thrown = act.Should().Throw<DecryptException>().Which;
        if (garbage.Length > 0) thrown.Message.Should().NotContain(garbage);
    }

    [Fact]
    public void TryDecrypt_returns_false_instead_of_throwing()
    {
        var encrypter = Ephemeral();

        encrypter.TryDecrypt("garbage", out var failed).Should().BeFalse();
        failed.Should().BeNull();

        encrypter.TryDecrypt(encrypter.Encrypt("ok"), out var value).Should().BeTrue();
        value.Should().Be("ok");
    }

    [Fact]
    public void A_null_argument_is_a_caller_error_not_a_DecryptException()
    {
        var encrypter = Ephemeral();

        ((Action)(() => encrypter.Encrypt((string)null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => encrypter.Decrypt(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => encrypter.ForPurpose(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Purposes_cannot_read_each_other()
    {
        var encrypter = Ephemeral();
        var billing = encrypter.ForPurpose("billing");
        var shipping = encrypter.ForPurpose("shipping");

        var token = billing.Encrypt("card");

        billing.Decrypt(token).Should().Be("card");
        ((Action)(() => shipping.Decrypt(token))).Should().Throw<DecryptException>();
        ((Action)(() => encrypter.Decrypt(token))).Should().Throw<DecryptException>();
        ((Action)(() => billing.Decrypt(encrypter.Encrypt("card")))).Should().Throw<DecryptException>();
    }

    [Fact]
    public void ForPurpose_is_stable_for_the_same_name()
    {
        var encrypter = Ephemeral();

        encrypter.ForPurpose("billing").Decrypt(encrypter.ForPurpose("billing").Encrypt("x")).Should().Be("x");
    }

    [Fact]
    public void A_value_survives_a_new_provider_over_the_same_key_ring()
    {
        string token;
        using (var first = Persistent())
        {
            token = first.GetRequiredService<IEncrypter>().Encrypt("kept");
        }

        using var second = Persistent();

        second.GetRequiredService<IEncrypter>().Decrypt(token).Should().Be("kept");
    }

    [Fact]
    public void Another_application_name_cannot_read_the_value_even_over_the_same_key_ring()
    {
        string token;
        using (var first = Persistent("app-a"))
        {
            token = first.GetRequiredService<IEncrypter>().Encrypt("kept");
        }

        using var other = Persistent("app-b");

        ((Action)(() => other.GetRequiredService<IEncrypter>().Decrypt(token))).Should().Throw<DecryptException>();
    }

    [Fact]
    public void A_value_from_an_unrelated_ephemeral_key_ring_cannot_be_read()
    {
        var token = Ephemeral().Encrypt("kept");

        ((Action)(() => Ephemeral().Decrypt(token))).Should().Throw<DecryptException>().Which.Message.Should().Contain("key ring").And.Contain("application name");
    }

    [Fact]
    public void A_decrypted_value_of_the_wrong_type_fails_without_quoting_the_plaintext()
    {
        var encrypter = Ephemeral();
        var token = encrypter.Encrypt("{\"secretfield\": nope}");

        var act = () => encrypter.Decrypt<CardInfo>(token);

        act.Should().Throw<DecryptException>().Which.Message.Should().NotContain("secretfield").And.NotContain("nope");
    }

    [Fact]
    public void AddNaravelEncryption_keeps_the_applications_own_data_protection()
    {
        var own = new EphemeralDataProtectionProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(own);
        services.AddNaravelEncryption();
        services.AddNaravelEncryption();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IEncrypter>().Should().ContainSingle();
        provider.GetRequiredService<IDataProtectionProvider>().Should().BeSameAs(own);
    }

    [Fact]
    public void AddNaravelEncryption_registers_data_protection_when_the_application_did_not()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelEncryption();
        using var provider = services.BuildServiceProvider();

        provider.GetService<IDataProtectionProvider>().Should().NotBeNull();
        provider.GetRequiredService<IEncrypter>().Should().NotBeNull();
    }

    [Fact]
    public void Operations_are_counted_with_only_the_operation_and_outcome_tags()
    {
        var encrypter = Ephemeral();
        var seen = new List<(string Operation, string Outcome, int TagCount)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Naravel.Encryption") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? operation = null, outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation") operation = (string?)tag.Value;
                if (tag.Key == "outcome") outcome = (string?)tag.Value;
            }

            lock (seen) seen.Add((operation!, outcome!, tags.Length));
        });
        listener.Start();

        var token = encrypter.Encrypt("x");
        encrypter.Decrypt(token);
        encrypter.TryDecrypt("garbage", out _);

        lock (seen)
        {
            seen.Should().Contain(("encrypt", "ok", 2)).And.Contain(("decrypt", "ok", 2)).And.Contain(("decrypt", "invalid", 2));
        }
    }
}
