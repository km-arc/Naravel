using System.Runtime.CompilerServices;
using Naravel.Queue.Batching;
using Naravel.Queue.Drivers;
using Naravel.Queue.Redis;

namespace Naravel.Queue.Providers.Tests;

/// <summary>
/// Owner decision AD-020 (option A): the token of the Redis queue driver, the Redis batch repository and the Redis failed-job store is checked
/// before a command is sent. StackExchange.Redis takes no token, so a command that was already sent is awaited and is never cancelled halfway.
/// These tests need no Redis server. The objects are built without a connection (the driver with a null connection, the other two without running
/// their constructors), so a method that sent a request before it looked at a cancelled token would fail with a <see cref="NullReferenceException"/>
/// and not with the <see cref="OperationCanceledException"/> asserted here.
/// </summary>
public sealed class RedisCancellationContractTests
{
    private static readonly QueuedMessage Message = new() { Queue = "jobs", JobType = "job", Payload = "{}" };

    [Fact]
    public async Task Every_method_of_the_queue_driver_checks_a_cancelled_token_before_it_sends_a_command()
    {
        var driver = new RedisQueueDriver(null!, TimeSpan.FromMinutes(1));

        await AssertAllCancelledAsync(new Dictionary<string, Func<CancellationToken, Task>>
        {
            ["PushAsync"] = token => driver.PushAsync(Message, token),
            ["PopAsync"] = token => driver.PopAsync("jobs", token),
            ["AckAsync"] = token => driver.AckAsync(Message, token),
            ["ReleaseAsync"] = token => driver.ReleaseAsync(Message, TimeSpan.Zero, token),
            ["FailAsync"] = token => driver.FailAsync(Message, token),
            ["TryFailAsync"] = token => driver.TryFailAsync(Message, token),
            ["SizeAsync"] = token => driver.SizeAsync("jobs", token)
        });
    }

    [Fact]
    public async Task Every_method_of_the_batch_repository_checks_a_cancelled_token_before_it_sends_a_command()
    {
        var repository = (RedisBatchRepository)RuntimeHelpers.GetUninitializedObject(typeof(RedisBatchRepository));
        var batch = new QueueBatch { TotalJobs = 1 };

        await AssertAllCancelledAsync(new Dictionary<string, Func<CancellationToken, Task>>
        {
            ["RegisterAsync"] = token => repository.RegisterAsync(batch, new BatchOptions(), token),
            ["GetAsync"] = token => repository.GetAsync(batch.Id, token),
            ["IsCancelledAsync"] = token => repository.IsCancelledAsync(batch.Id, token),
            ["CancelAsync"] = token => repository.CancelAsync(batch.Id, token),
            ["MarkJobCompletedAsync"] = token => repository.MarkJobCompletedAsync(batch.Id, token),
            ["MarkJobFailedAsync"] = token => repository.MarkJobFailedAsync(batch.Id, new InvalidOperationException("failed"), token),
            ["MarkJobCancelledAsync"] = token => repository.MarkJobCancelledAsync(batch.Id, token),
            ["ResumePendingCallbacksAsync"] = token => repository.ResumePendingCallbacksAsync(token)
        });
    }

    [Fact]
    public async Task Every_method_of_the_failed_job_store_checks_a_cancelled_token_before_it_sends_a_command()
    {
        var store = (RedisFailedJobStore)RuntimeHelpers.GetUninitializedObject(typeof(RedisFailedJobStore));

        await AssertAllCancelledAsync(new Dictionary<string, Func<CancellationToken, Task>>
        {
            ["RecordAsync"] = token => store.RecordAsync(Message, new InvalidOperationException("failed"), token),
            ["ListAsync"] = token => store.ListAsync(token),
            ["ForgetAsync"] = token => store.ForgetAsync(Message.Id, token),
            ["FlushAsync"] = token => store.FlushAsync(token)
        });
    }

    private static async Task AssertAllCancelledAsync(Dictionary<string, Func<CancellationToken, Task>> calls)
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        foreach (var (name, call) in calls)
        {
            var act = () => call(source.Token);
            var thrown = await act.Should().ThrowAsync<OperationCanceledException>($"{name} must look at the token before it sends a command");
            thrown.Which.CancellationToken.Should().Be(source.Token, $"{name} reports the caller's token");
        }
    }
}
