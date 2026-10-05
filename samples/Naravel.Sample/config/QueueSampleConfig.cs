using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;
using Naravel.Queue.File;
using Naravel.Queue.Jobs;
using Naravel.Queue.Kafka;
using Naravel.Queue.Memory;
using Naravel.Queue.RabbitMQ;
using Naravel.Queue.Redis;
using Naravel.Sample.App.Jobs;

namespace Naravel.Sample.Config;

public static class QueueSampleConfig
{
    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        var sampleConnection = configuration["NaravelQueue:SampleConnection"] ?? "file";
        services.AddQueue(configuration)
            .AddMemoryDriver(configuration)
            .AddFileDriver(configuration)
            .AddRedisDriver(configuration)
            .AddRabbitMqDriver(configuration)
            .AddKafkaDriver(configuration);

        services.AddQueueWorker(worker =>
        {
            worker.Connection = sampleConnection;
            worker.Queues = new[] { "priority", "emails", "default", "reports" };
            worker.Concurrency = 2;
            worker.SleepWhenEmpty = TimeSpan.FromMilliseconds(500);
        });

        services.AddQueueWorker(worker =>
        {
            worker.Connection = "sync";
            worker.Queues = new[] { "internal" };
            worker.Concurrency = 1;
            worker.SleepWhenEmpty = TimeSpan.FromMilliseconds(250);
        });
    }

    public static async Task DispatchDemoJobsAsync(IJobDispatcher dispatcher, string connection = "file")
    {
        await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com", "Ali"), options => options
            .OnConnection(connection)
            .OnQueue("default"));

        await dispatcher.DispatchAsync(new SendWelcomeEmailJob("sara@example.com", "Sara"), options => options
            .OnConnection(connection)
            .OnQueue("emails")
            .DelayFor(TimeSpan.FromSeconds(2))
            .WithPriority(9)
            .WithMaxAttempts(4));

        await dispatcher.DispatchAsync(new FlakyReportJob("Monthly Sales"), options => options
            .OnConnection(connection)
            .OnQueue("reports")
            .WithPriority(5));

        await dispatcher.DispatchAsync(new GenerateInvoiceJob("ORD-1001"), options => options
            .OnConnection(connection)
            .OnQueue("default"));

        await dispatcher.Chain(
            new GenerateInvoiceJob("ORD-1002"),
            new SendInvoiceEmailJob("ORD-1002"))
            .DispatchAsync(options => options
                .OnConnection(connection)
                .OnQueue("default"));

        await dispatcher.BatchAsync(
            jobs: new IJob[]
            {
                new SendWelcomeEmailJob("batch-a@example.com", "A"),
                new SendWelcomeEmailJob("batch-b@example.com", "B"),
                new SendWelcomeEmailJob("batch-c@example.com", "C"),
            },
            configure: options => options
                .OnConnection(connection)
                .OnQueue("emails")
                .WithPriority(8),
            batchConfigure: batch =>
            {
                batch.OnCompleted = (b, ct) =>
                {
                    Console.WriteLine($"Batch {b.Id} completed: {b.CompletedJobs} succeeded, {b.FailedJobs} failed.");
                    return Task.CompletedTask;
                };
            });

        await dispatcher.DispatchAsync(new PriorityAlertJob("Ops: Payment gateway latency spike"), options => options
            .OnConnection(connection)
            .OnQueue("priority")
            .WithPriority(9));

        await dispatcher.DispatchAsync(new BatchSummaryJob("Daily digest generated"), options => options
            .OnConnection("sync")
            .OnQueue("internal"));
    }
}
