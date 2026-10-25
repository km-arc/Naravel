using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Encryption;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Failed;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Encryption.Tests;

public sealed class SecretJob : Job, IShouldBeEncrypted
{
    public string Secret { get; set; } = string.Empty;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        context.Services.GetRequiredService<ConcurrentQueue<string>>().Enqueue(Secret);
        return Task.CompletedTask;
    }
}

public sealed class FailingSecretJob : Job, IShouldBeEncrypted
{
    public string Secret { get; set; } = string.Empty;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("always fails");
}

public sealed class PlainJob : Job
{
    public string Note { get; set; } = string.Empty;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        context.Services.GetRequiredService<ConcurrentQueue<string>>().Enqueue(Note);
        return Task.CompletedTask;
    }
}

public sealed class EncryptedJobTests
{
    private const string Secret = "card-4242-4242-4242-4242";

    private static ServiceCollection Services(IDataProtectionProvider? keys = null, Action<QueueEncryptionOptions>? configure = null, bool encryption = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NaravelQueue:Default"] = "q",
                ["NaravelQueue:Stores:q:Driver"] = "memory"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(keys ?? new EphemeralDataProtectionProvider());
        services.AddSingleton(new ConcurrentQueue<string>());
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddJob<SecretJob>("secret");
        services.AddJob<FailingSecretJob>("secret.failing");
        services.AddJob<PlainJob>("plain");
        if (encryption) services.AddNaravelQueueEncryption(configure);
        return services;
    }

    [Fact]
    public void A_marked_job_is_stored_as_an_envelope_that_does_not_contain_the_plaintext()
    {
        using var provider = Services().BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();

        var stored = serializer.Serialize(new SecretJob { Secret = Secret });

        stored.Should().StartWith("naravel:enc:v1:").And.NotContain(Secret).And.NotContain("Secret");
    }

    [Fact]
    public void An_unmarked_job_keeps_the_existing_wire_format()
    {
        using var provider = Services().BuildServiceProvider();
        var encrypted = provider.GetRequiredService<IJobSerializer>();
        using var plainProvider = Services(encryption: false).BuildServiceProvider();
        var plain = plainProvider.GetRequiredService<IJobSerializer>();
        var job = new PlainJob { Note = "hello" };

        encrypted.Serialize(job).Should().Be(plain.Serialize(job)).And.Contain("hello");
    }

    [Fact]
    public void A_marked_job_round_trips_through_the_serializer()
    {
        using var provider = Services().BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();

        var job = (SecretJob)serializer.Deserialize(serializer.Serialize(new SecretJob { Secret = Secret }), typeof(SecretJob));

        job.Secret.Should().Be(Secret);
    }

    [Fact]
    public void The_type_is_chosen_by_the_alias_registry_only()
    {
        using var provider = Services().BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();

        serializer.GetTypeName(typeof(SecretJob)).Should().Be("secret");
        serializer.ResolveType("secret").Should().Be(typeof(SecretJob));
        ((Action)(() => serializer.ResolveType("Naravel.Something.EvilType, Naravel"))).Should().Throw<Exception>();
    }

    [Fact]
    public void A_tampered_envelope_fails_visibly_and_names_the_key_ring_first()
    {
        using var provider = Services().BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();
        var stored = serializer.Serialize(new SecretJob { Secret = Secret });
        var tampered = stored[..^4] + (stored.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var act = () => serializer.Deserialize(tampered, typeof(SecretJob));

        var message = act.Should().Throw<DecryptException>().Which.Message;
        message.Should().Contain("key ring").And.Contain("application name").And.NotContain(Secret);
        message.IndexOf("key ring", StringComparison.Ordinal).Should().BeLessThan(message.IndexOf("altered", StringComparison.Ordinal));
    }

    [Fact]
    public void A_worker_with_another_key_ring_cannot_read_the_job_and_nothing_falls_back_to_plaintext()
    {
        using var producer = Services().BuildServiceProvider();
        using var worker = Services().BuildServiceProvider();
        var stored = producer.GetRequiredService<IJobSerializer>().Serialize(new SecretJob { Secret = Secret });

        var act = () => worker.GetRequiredService<IJobSerializer>().Deserialize(stored, typeof(SecretJob));

        act.Should().Throw<DecryptException>().Which.Message.Should().Contain("key ring").And.Contain("application name");
    }

    [Fact]
    public void A_worker_sharing_the_key_ring_reads_the_job()
    {
        var keys = new EphemeralDataProtectionProvider();
        using var producer = Services(keys).BuildServiceProvider();
        using var worker = Services(keys).BuildServiceProvider();
        var stored = producer.GetRequiredService<IJobSerializer>().Serialize(new SecretJob { Secret = Secret });

        var job = (SecretJob)worker.GetRequiredService<IJobSerializer>().Deserialize(stored, typeof(SecretJob));

        job.Secret.Should().Be(Secret);
    }

    [Fact]
    public void An_unknown_envelope_version_fails()
    {
        using var provider = Services().BuildServiceProvider();

        var act = () => provider.GetRequiredService<IJobSerializer>().Deserialize("naravel:enc:v9:abcdef", typeof(SecretJob));

        act.Should().Throw<DecryptException>().Which.Message.Should().Contain("envelope version");
    }

    [Fact]
    public void A_job_that_the_envelope_purpose_did_not_write_is_refused()
    {
        var keys = new EphemeralDataProtectionProvider();
        using var provider = Services(keys).BuildServiceProvider();
        var foreign = "naravel:enc:v1:" + keys.CreateProtector("some.other.purpose").Protect("{\"Secret\":\"x\"}");

        var act = () => provider.GetRequiredService<IJobSerializer>().Deserialize(foreign, typeof(SecretJob));

        act.Should().Throw<DecryptException>();
    }

    [Fact]
    public void A_value_from_IEncrypter_is_never_accepted_as_a_job_payload()
    {
        var keys = new EphemeralDataProtectionProvider();
        using var provider = Services(keys).BuildServiceProvider();
        var facade = provider.GetRequiredService<IEncrypter>().Encrypt("{\"Secret\":\"x\"}");

        var act = () => provider.GetRequiredService<IJobSerializer>().Deserialize("naravel:enc:v1:" + facade, typeof(SecretJob));

        act.Should().Throw<DecryptException>();
    }

    [Fact]
    public void A_plaintext_payload_for_a_marked_class_is_refused_by_default()
    {
        using var provider = Services().BuildServiceProvider();

        var act = () => provider.GetRequiredService<IJobSerializer>().Deserialize("{\"Secret\":\"old\"}", typeof(SecretJob));

        act.Should().Throw<DecryptException>().Which.Message.Should().Contain("AcceptPlaintextForMarkedJobs");
    }

    [Fact]
    public void A_plaintext_payload_for_a_marked_class_is_accepted_under_the_migration_option()
    {
        using var provider = Services(configure: options => options.AcceptPlaintextForMarkedJobs = true).BuildServiceProvider();

        var job = (SecretJob)provider.GetRequiredService<IJobSerializer>().Deserialize("{\"Secret\":\"old\"}", typeof(SecretJob));

        job.Secret.Should().Be("old");
    }

    [Fact]
    public void An_unmarked_class_still_reads_a_payload_that_was_encrypted_before_the_marker_was_removed()
    {
        using var provider = Services().BuildServiceProvider();
        var serializer = provider.GetRequiredService<IJobSerializer>();
        var stored = serializer.Serialize(new SecretJob { Secret = Secret });

        var job = (PlainJob)serializer.Deserialize(stored, typeof(PlainJob));

        job.Note.Should().BeEmpty();
    }

    [Fact]
    public void AddNaravelQueueEncryption_needs_the_queue_first_and_is_idempotent()
    {
        var act = () => new ServiceCollection().AddNaravelQueueEncryption();
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddQueue*");

        var services = Services();
        services.AddNaravelQueueEncryption();
        services.Count(descriptor => descriptor.ServiceType == typeof(IJobSerializer)).Should().Be(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IJobSerializer>().Serialize(new SecretJob { Secret = Secret }).Should().StartWith("naravel:enc:v1:");
    }

    [Fact]
    public async Task The_message_in_the_queue_is_ciphertext_and_the_worker_still_runs_the_job()
    {
        var services = Services();
        services.AddQueueWorker(options =>
        {
            options.Connection = "q";
            options.Queues = ["default"];
            options.Rest = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IJobDispatcher>();

        await dispatcher.DispatchAsync(new SecretJob { Secret = Secret });
        var queued = await provider.GetRequiredService<QueueManager>().Connection("q").PopAsync("default");
        queued.Should().NotBeNull();
        queued!.Payload.Should().StartWith("naravel:enc:v1:").And.NotContain(Secret);
        JsonSerializer.Serialize(queued).Should().NotContain(Secret);
        await dispatcher.DispatchAsync(new SecretJob { Secret = Secret });

        var ran = provider.GetRequiredService<ConcurrentQueue<string>>();
        var workers = provider.GetServices<IHostedService>().ToArray();
        foreach (var worker in workers) await worker.StartAsync(CancellationToken.None);
        try
        {
            (await WaitUntilAsync(() => ran.Count == 1)).Should().BeTrue();
            ran.Single().Should().Be(Secret);
        }
        finally
        {
            foreach (var worker in workers) await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_chain_carries_ciphertext_for_its_remaining_links()
    {
        await using var provider = Services().BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IJobDispatcher>();

        await dispatcher.DispatchChainAsync([new PlainJob { Note = "first" }, new SecretJob { Secret = Secret }], null, CancellationToken.None);
        var queued = await provider.GetRequiredService<QueueManager>().Connection("q").PopAsync("default");

        queued.Should().NotBeNull();
        queued!.ChainedJobPayload.Should().NotBeNull().And.Contain("naravel:enc:v1:").And.NotContain(Secret);
    }

    [Fact]
    public async Task A_failed_job_is_recorded_with_its_ciphertext_not_its_plaintext()
    {
        var services = Services();
        services.AddQueueWorker(options =>
        {
            options.Connection = "q";
            options.Queues = ["default"];
            options.Rest = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IFailedJobStore>();
        var workers = provider.GetServices<IHostedService>().ToArray();
        foreach (var worker in workers) await worker.StartAsync(CancellationToken.None);
        try
        {
            await provider.GetRequiredService<IJobDispatcher>().DispatchAsync(new FailingSecretJob { Secret = Secret }, options => options.WithMaxAttempts(1));

            (await WaitUntilAsync(() => store.ListAsync(CancellationToken.None).GetAwaiter().GetResult().Count == 1)).Should().BeTrue();
            var failed = (await store.ListAsync(CancellationToken.None)).Single();
            failed.Payload.Should().StartWith("naravel:enc:v1:").And.NotContain(Secret);
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
