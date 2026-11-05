using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;
using Naravel.Queue.Serialization;
using Naravel.Queue.Testing;

namespace Naravel.Queue.Tests;

// Runs the quickstart and the registration and QueueFake messages of docs/en/queue.md and docs/fa/queue.md.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    public sealed class SendWelcomeEmailJob : Job
    {
        public static TaskCompletionSource<string> Handled { get; set; } = new();

        public string ToEmail { get; set; } = default!;

        public SendWelcomeEmailJob() { }
        public SendWelcomeEmailJob(string toEmail) => ToEmail = toEmail;

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Handled.TrySetResult($"{ToEmail}:{context.Attempt}");
            return Task.CompletedTask;
        }
    }

    public sealed class FirstJob : Job
    {
        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class SecondJob : Job
    {
        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IConfiguration MemoryConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "local",
            ["NaravelQueue:Stores:local:Driver"] = "memory",
        }).Build();

    [Fact]
    public async Task The_quickstart_job_runs_on_the_memory_worker()
    {
        var configuration = MemoryConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddQueueWorker();

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted) await service.StartAsync(CancellationToken.None);
        try
        {
            SendWelcomeEmailJob.Handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            var id = await provider.GetRequiredService<IJobDispatcher>().DispatchAsync(new SendWelcomeEmailJob("ada@example.com"));

            id.Should().NotBeNullOrEmpty();
            (await SendWelcomeEmailJob.Handled.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("ada@example.com:1");
        }
        finally
        {
            foreach (var service in hosted) await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void The_registry_errors_carry_the_messages_in_the_guide()
    {
        var configuration = MemoryConfiguration();

        var duplicate = new ServiceCollection();
        duplicate.AddQueue(configuration);
        duplicate.AddJob<FirstJob>("mail.welcome");
        duplicate.AddJob<SecondJob>("mail.welcome");
        using var duplicateProvider = duplicate.BuildServiceProvider();
        FluentActions.Invoking(() => duplicateProvider.GetRequiredService<IJobTypeRegistry>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Queue job alias 'mail.welcome' is already registered for '*FirstJob'.");

        var single = new ServiceCollection();
        single.AddQueue(configuration);
        single.AddJob<FirstJob>("mail.welcome");
        using var singleProvider = single.BuildServiceProvider();
        FluentActions.Invoking(() => singleProvider.GetRequiredService<IJobTypeRegistry>().Resolve("mail.goodbye"))
            .Should().Throw<UnknownJobTypeException>()
            .WithMessage("Queue job type alias 'mail.goodbye' is not registered.");
    }

    [Fact]
    public async Task QueueFake_assertions_fail_with_the_messages_in_the_guide()
    {
        var queue = new QueueFake();
        FluentActions.Invoking(() => queue.AssertDispatched<SendWelcomeEmailJob>())
            .Should().Throw<InvalidOperationException>().WithMessage("No SendWelcomeEmailJob job was dispatched.");

        await queue.DispatchAsync(new SendWelcomeEmailJob("ada@example.com"));

        FluentActions.Invoking(() => queue.AssertNotDispatched<SendWelcomeEmailJob>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Expected no SendWelcomeEmailJob job to be dispatched, but 1 was.");
        FluentActions.Invoking(() => queue.AssertDispatchedCount<SendWelcomeEmailJob>(2))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Expected 2 SendWelcomeEmailJob dispatch(es), but found 1.");
        FluentActions.Invoking(() => queue.AssertChained<SendWelcomeEmailJob>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("No SendWelcomeEmailJob job was found in a dispatched chain.");
    }
}
