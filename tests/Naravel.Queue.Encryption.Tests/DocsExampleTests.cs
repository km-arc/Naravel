using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Encryption;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Failed;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Encryption.Tests;

// Runs the queue parts of docs/en/encryption.md: the job of "Encrypted queue jobs", its "Errors" and its "Testing" examples.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    private const string Card = "4242424242424242";

    private const string Failure =
        "A queued job payload could not be decrypted. First check that this worker and the application that queued the job share the same Data Protection key ring "
        + "and application name; then check that the payload was not altered and that every worker runs a version that understands the envelope.";

    private const string UnknownVersion =
        "A queued job payload uses an encryption envelope version this worker does not understand. Update the worker, or check that the producer and worker run compatible versions "
        + "and share the same Data Protection key ring and application name.";

    private const string PlaintextRefused =
        "A job of a class marked IShouldBeEncrypted was stored as plaintext and was refused. If it was queued before the class was marked, let it drain or set "
        + "QueueEncryptionOptions.AcceptPlaintextForMarkedJobs for a deploy window; otherwise treat the message as untrusted.";

    // The job of the guide.
    public sealed class ChargeCard : Job, IShouldBeEncrypted
    {
        public string CardNumber { get; set; } = "";

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;   // your work
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["NaravelQueue:Default"] = "local",
        ["NaravelQueue:Stores:local:Driver"] = "memory"
    }).Build();

    // The registrations of the "Testing" section, before BuildServiceProvider: `encryption` and `configure` let the tests vary the registration.
    private static ServiceCollection Services(IDataProtectionProvider? keys = null, bool encryption = true, Action<QueueEncryptionOptions>? configure = null)
    {
        var configuration = Configuration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(keys ?? new EphemeralDataProtectionProvider());
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddJob<ChargeCard>("charge-card");
        if (encryption) services.AddNaravelQueueEncryption(configure);
        return services;
    }

    // The helper of the "Testing" section, as written there.
    private static IJobSerializer NewSerializer(IDataProtectionProvider? keys = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "local",
            ["NaravelQueue:Stores:local:Driver"] = "memory"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(keys ?? new EphemeralDataProtectionProvider());
        services.AddQueue(configuration).AddMemoryDriver(configuration);   // your application's queue registrations
        services.AddJob<ChargeCard>("charge-card");
        services.AddNaravelQueueEncryption();
        return services.BuildServiceProvider().GetRequiredService<IJobSerializer>();
    }

    private static IJobSerializer SerializerOf(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IJobSerializer>();

    private static string Tamper(string payload) => payload[..^4] + (payload.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

    private static DecryptException Refused(IJobSerializer serializer, string payload) =>
        ((Action)(() => serializer.Deserialize(payload, typeof(ChargeCard)))).Should().Throw<DecryptException>().Which;

    // The two tests of the "Testing" section, as written there.
    [Fact]
    public void A_charge_card_job_is_stored_as_an_envelope_and_reads_back()
    {
        var serializer = NewSerializer();

        var stored = serializer.Serialize(new ChargeCard { CardNumber = "4242424242424242" });

        stored.Should().StartWith("naravel:enc:v1:").And.NotContain("4242");
        ((ChargeCard)serializer.Deserialize(stored, typeof(ChargeCard))).CardNumber.Should().Be("4242424242424242");
    }

    [Fact]
    public void A_worker_reads_the_job_only_when_it_shares_the_key_ring()
    {
        var keys = new EphemeralDataProtectionProvider();
        var stored = NewSerializer(keys).Serialize(new ChargeCard { CardNumber = "4242424242424242" });

        NewSerializer(keys).Deserialize(stored, typeof(ChargeCard)).Should().BeOfType<ChargeCard>();

        var act = () => NewSerializer().Deserialize(stored, typeof(ChargeCard));   // a new key ring
        act.Should().Throw<DecryptException>().Which.Message.Should().Contain("key ring");
    }

    [Fact]
    public void Registering_needs_services_and_the_queue_first_and_a_second_call_does_not_run_its_configure()
    {
        var nullServices = () => ((IServiceCollection)null!).AddNaravelQueueEncryption();
        nullServices.Should().ThrowExactly<ArgumentNullException>().Which.ParamName.Should().Be("services");

        var beforeTheQueue = () => new ServiceCollection().AddNaravelQueueEncryption();
        beforeTheQueue.Should().ThrowExactly<InvalidOperationException>()
            .Which.Message.Should().Be("No IJobSerializer is registered. Call AddQueue before AddNaravelQueueEncryption.");

        var services = Services();                                                                  // the first call, with no options
        services.AddNaravelQueueEncryption(options => options.AcceptPlaintextForMarkedJobs = true);   // the second call does nothing
        services.Count(descriptor => descriptor.ServiceType == typeof(IJobSerializer)).Should().Be(1);
        Refused(SerializerOf(services), "{\"CardNumber\":\"old\"}").Message.Should().Be(PlaintextRefused);

        using var once = Services(configure: options => options.AcceptPlaintextForMarkedJobs = true).BuildServiceProvider();
        ((ChargeCard)once.GetRequiredService<IJobSerializer>().Deserialize("{\"CardNumber\":\"old\"}", typeof(ChargeCard))).CardNumber.Should().Be("old");
    }

    [Fact]
    public void A_payload_that_a_worker_cannot_open_gives_the_fixed_message_of_its_kind()
    {
        var keys = new EphemeralDataProtectionProvider();
        var worker = SerializerOf(Services(keys));
        var stored = worker.Serialize(new ChargeCard { CardNumber = Card });
        var cases = new (string Cause, string Payload, string Message)[]
        {
            ("a changed text", Tamper(stored), Failure),
            ("text that is not base64 after the prefix", "naravel:enc:v1:not a token", Failure),
            ("another key ring", SerializerOf(Services()).Serialize(new ChargeCard { CardNumber = Card }), Failure),
            ("another purpose", "naravel:enc:v1:" + keys.CreateProtector("some.other.purpose").Protect("{\"CardNumber\":\"x\"}"), Failure),
            ("a value made by IEncrypter", "naravel:enc:v1:" + Services(keys).BuildServiceProvider().GetRequiredService<IEncrypter>().Encrypt("{\"CardNumber\":\"x\"}"), Failure),
            ("another envelope version", "naravel:enc:v9:abcdef", UnknownVersion),
            ("a longer version number", "naravel:enc:v10:abcdef", UnknownVersion),
            ("plaintext for a marked class", "{\"CardNumber\":\"" + Card + "\"}", PlaintextRefused)
        };

        foreach (var (cause, payload, message) in cases)
        {
            var error = Refused(worker, payload);

            error.Message.Should().Be(message, cause);
            error.ToString().Should().NotContain(Card, cause);
        }

        Refused(worker, Tamper(stored)).InnerException.Should().BeAssignableTo<CryptographicException>();
    }

    [Fact]
    public void A_producer_without_the_registration_sends_plaintext_and_a_worker_with_it_refuses_the_job()
    {
        var keys = new EphemeralDataProtectionProvider();
        var producer = SerializerOf(Services(keys, encryption: false));

        var stored = producer.Serialize(new ChargeCard { CardNumber = Card });

        stored.Should().Contain(Card).And.NotStartWith("naravel:enc:");
        Refused(SerializerOf(Services(keys)), stored).Message.Should().Be(PlaintextRefused);
        var migrating = SerializerOf(Services(keys, configure: options => options.AcceptPlaintextForMarkedJobs = true));
        ((ChargeCard)migrating.Deserialize(stored, typeof(ChargeCard))).CardNumber.Should().Be(Card);
    }

    [Fact]
    public void A_worker_without_the_registration_reads_an_envelope_as_json_and_fails_with_a_JsonException()
    {
        var keys = new EphemeralDataProtectionProvider();
        var stored = SerializerOf(Services(keys)).Serialize(new ChargeCard { CardNumber = Card });
        var worker = SerializerOf(Services(keys, encryption: false));

        var act = () => worker.Deserialize(stored, typeof(ChargeCard));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task A_job_that_cannot_be_read_is_recorded_as_failed_with_its_ciphertext_and_the_exception_text()
    {
        var services = Services();
        services.AddQueueWorker(options =>
        {
            options.Connection = "local";
            options.Queues = ["default"];
            options.Rest = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var changed = Tamper(provider.GetRequiredService<IJobSerializer>().Serialize(new ChargeCard { CardNumber = Card }));
        await provider.GetRequiredService<QueueManager>().Connection("local").PushAsync(new QueuedMessage
        {
            Queue = "default",
            Connection = "local",
            JobType = "charge-card",
            Payload = changed,
            MaxAttempts = 1
        });
        var store = provider.GetRequiredService<IFailedJobStore>();
        var workers = provider.GetServices<IHostedService>().ToArray();
        foreach (var worker in workers) await worker.StartAsync(CancellationToken.None);
        try
        {
            (await WaitUntilAsync(() => store.ListAsync(CancellationToken.None).GetAwaiter().GetResult().Count == 1)).Should().BeTrue();

            var failed = (await store.ListAsync(CancellationToken.None)).Single();
            failed.Payload.Should().Be(changed).And.StartWith("naravel:enc:v1:").And.NotContain(Card);
            failed.Error.Should().Contain("DecryptException").And.Contain(Failure).And.NotContain(Card);
        }
        finally
        {
            foreach (var worker in workers) await worker.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
    }
}
