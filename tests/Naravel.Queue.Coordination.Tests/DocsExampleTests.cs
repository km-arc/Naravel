using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;
using Naravel.Queue.Middleware;
using Naravel.Queue.Testing;

namespace Naravel.Queue.Coordination.Tests;

// Runs the quickstart, the unique job example, the Testing example and the messages of docs/en/queue-coordination.md.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    public sealed class SyncInvoiceJob : Job, IHasJobMiddleware
    {
        public static TaskCompletionSource<int> Handled { get; set; } = new();

        public int InvoiceId { get; set; }

        public IEnumerable<IJobMiddleware> Middleware(JobContext context) => new IJobMiddleware[]
        {
            new RateLimited("billing-api", maxAttempts: 60, window: TimeSpan.FromMinutes(1)),
            new WithoutOverlapping($"invoice:{InvoiceId}")
        };

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Handled.TrySetResult(InvoiceId);
            return Task.CompletedTask;
        }
    }

    public sealed class BuildReportJob : Job, IUniqueJob
    {
        public int ReportId { get; set; }

        public string UniqueKey => $"report:{ReportId}";
        public TimeSpan UniqueFor => TimeSpan.FromMinutes(10);

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ZeroLifetimeJob : Job, IUniqueJob
    {
        public string UniqueKey => "zero";
        public TimeSpan UniqueFor => TimeSpan.Zero;

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class EmptyKeyJob : Job, IUniqueJob
    {
        public string UniqueKey => "";

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IConfiguration QueueConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "local",
            ["NaravelQueue:Stores:local:Driver"] = "memory",
        }).Build();

    // The registration of the quickstart. The configuration has no "Cache" section, which gives the memory store and lock.
    private static ServiceCollection Quickstart(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(configuration);
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddQueueCoordination();
        services.AddQueueWorker();
        return services;
    }

    [Fact]
    public async Task The_quickstart_job_runs_through_its_middleware_on_the_memory_worker()
    {
        await using var provider = Quickstart(QueueConfiguration()).BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted) await service.StartAsync(CancellationToken.None);
        try
        {
            SyncInvoiceJob.Handled = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var id = await provider.GetRequiredService<IJobDispatcher>().DispatchAsync(new SyncInvoiceJob { InvoiceId = 7 });

            Assert.False(string.IsNullOrEmpty(id));
            Assert.Equal(7, await SyncInvoiceJob.Handled.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            foreach (var service in hosted) await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_second_dispatch_of_a_unique_job_returns_null_and_another_key_does_not()
    {
        // No hosted service is started, so nothing takes the first job off the queue while the others are dispatched.
        await using var provider = Quickstart(QueueConfiguration()).BuildServiceProvider();
        var jobs = provider.GetRequiredService<IJobDispatcher>();

        var first = await jobs.DispatchAsync(new BuildReportJob { ReportId = 7 });
        var duplicate = await jobs.DispatchAsync(new BuildReportJob { ReportId = 7 });
        var other = await jobs.DispatchAsync(new BuildReportJob { ReportId = 8 });

        first.Should().NotBeNullOrEmpty();
        duplicate.Should().BeNull();
        other.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Registering_without_a_cache_fails_with_the_message_in_the_guide()
    {
        var configuration = QueueConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddQueueCoordination();
        using var provider = services.BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<IUniqueJobGate>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Naravel.Queue.Coordination needs a Cache lock and store. Call services.AddNaravelCache(configuration) " +
                         "(and a provider such as AddNaravelRedisCache for coordination across processes) before starting the host.");
    }

    [Fact]
    public async Task The_dispatch_errors_carry_the_messages_in_the_guide()
    {
        var configuration = QueueConfiguration();

        var withoutGate = new ServiceCollection();
        withoutGate.AddLogging();
        withoutGate.AddQueue(configuration).AddMemoryDriver(configuration);
        await using var plain = withoutGate.BuildServiceProvider();
        await FluentActions.Invoking(() => plain.GetRequiredService<IJobDispatcher>().DispatchAsync(new BuildReportJob { ReportId = 7 }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("BuildReportJob implements IUniqueJob, but no IUniqueJobGate is registered, so uniqueness cannot be enforced. " +
                         "Call services.AddQueueCoordination() (package Naravel.Queue.Coordination, which needs AddNaravelCache()).");

        await using var provider = Quickstart(configuration).BuildServiceProvider();
        var jobs = provider.GetRequiredService<IJobDispatcher>();

        FluentActions.Invoking(() => jobs.Chain(new BuildReportJob { ReportId = 7 }))
            .Should().Throw<ArgumentException>()
            .WithMessage("BuildReportJob implements IUniqueJob and cannot be part of a chain: a rejected duplicate would stall the sequence. " +
                         "Dispatch it on its own.*")
            .WithParameterName("jobs");

        await FluentActions.Invoking(() => jobs.DispatchAsync(new ZeroLifetimeJob()))
            .Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("The unique lock lifetime must be positive.*");

        var emptyKey = await FluentActions.Invoking(() => jobs.DispatchAsync(new EmptyKeyJob())).Should().ThrowAsync<ArgumentException>();
        emptyKey.Which.ParamName.Should().Be("key");
    }

    [Fact]
    public void The_middleware_arguments_are_checked_with_the_messages_in_the_guide()
    {
        FluentActions.Invoking(() => new WithoutOverlapping(" "))
            .Should().Throw<ArgumentException>().WithParameterName("key");
        FluentActions.Invoking(() => new WithoutOverlapping("k").ReleaseAfter(TimeSpan.FromSeconds(-1)))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("The delay cannot be negative.*");
        FluentActions.Invoking(() => new WithoutOverlapping("k").ExpireAfter(TimeSpan.Zero))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("The lock lifetime must be positive.*");

        FluentActions.Invoking(() => new RateLimited(" ", 1, TimeSpan.FromSeconds(1)))
            .Should().Throw<ArgumentException>().WithParameterName("limiter");
        FluentActions.Invoking(() => new RateLimited("billing-api", 0, TimeSpan.FromSeconds(1)))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxAttempts");
        FluentActions.Invoking(() => new RateLimited("billing-api", 1, TimeSpan.Zero))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("The window must be positive.*");

        FluentActions.Invoking(() => new ThrottlesExceptions(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxExceptions");
        FluentActions.Invoking(() => new ThrottlesExceptions(1, TimeSpan.Zero, TimeSpan.FromSeconds(1)))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("The window must be positive.*");
        FluentActions.Invoking(() => new ThrottlesExceptions(1, TimeSpan.FromSeconds(1), TimeSpan.Zero))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("The backoff must be positive.*");
    }

    [Fact]
    public async Task QueueFake_mimics_the_unique_gate_and_rejects_a_chain_with_its_own_message()
    {
        var queue = new QueueFake();

        (await queue.DispatchAsync(new BuildReportJob { ReportId = 7 })).Should().NotBeNull();
        (await queue.DispatchAsync(new BuildReportJob { ReportId = 7 })).Should().BeNull();
        queue.AssertDispatchedCount<BuildReportJob>(1);

        FluentActions.Invoking(() => queue.Chain(new BuildReportJob { ReportId = 8 }))
            .Should().Throw<ArgumentException>()
            .WithMessage("BuildReportJob implements IUniqueJob and cannot be part of a chain.*")
            .WithParameterName("jobs");

        queue.Reset();
        (await queue.DispatchAsync(new BuildReportJob { ReportId = 7 })).Should().NotBeNull();
    }
}
