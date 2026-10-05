using Naravel.Queue.Jobs;

namespace Naravel.Sample.App.Jobs;

/// <summary>
/// A basic job. Inheriting from Job gives you sensible retry defaults (3 attempts, backoff 5s/15s/60s).
/// Any public properties are serialized to JSON and rebuilt on the worker side, so keep payloads small
/// and serializable (ids, not whole entity graphs - same rule as Laravel).
/// </summary>
public class SendWelcomeEmailJob : Job
{
    public string ToEmail { get; set; } = default!;
    public string UserName { get; set; } = default!;

    // Every job can be pushed to a specific queue by default. Callers can still override at dispatch time.
    public override string Queue => "emails";

    public SendWelcomeEmailJob() { } // required for JSON deserialization
    public SendWelcomeEmailJob(string toEmail, string userName)
    {
        ToEmail = toEmail;
        UserName = userName;
    }

    public override async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        // Resolve any scoped/DI service you need right here, just like a controller action.
        // var mailer = context.Services.GetRequiredService<IEmailSender>();
        Console.WriteLine($"[Attempt {context.Attempt}/{context.MaxAttempts}] Sending welcome email to {ToEmail} ({UserName})...");
        await Task.Delay(200, cancellationToken);
        Console.WriteLine($"Welcome email sent to {ToEmail}.");
    }

    // Called once, only after all attempts are exhausted.
    public override Task FailedAsync(JobContext context, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Giving up on welcome email to {ToEmail}: {exception.Message}");
        return Task.CompletedTask;
    }
}

/// <summary>A job that intentionally fails a couple of times to demonstrate retry/backoff.</summary>
public class FlakyReportJob : Job
{
    public string ReportName { get; set; } = default!;
    public override int MaxAttempts => 4;
    public override TimeSpan[] Backoff => new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

    public FlakyReportJob() { }
    public FlakyReportJob(string reportName) => ReportName = reportName;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Building report '{ReportName}' (attempt {context.Attempt}/{context.MaxAttempts})...");
        if (context.Attempt < 3)
            throw new InvalidOperationException("Simulated transient failure - pretend the report service was unavailable.");

        Console.WriteLine($"Report '{ReportName}' built successfully.");
        return Task.CompletedTask;
    }
}

/// <summary>Demonstrates chaining: this job dynamically queues a follow-up job from inside HandleAsync.</summary>
public class GenerateInvoiceJob : Job
{
    public string OrderId { get; set; } = default!;

    public GenerateInvoiceJob() { }
    public GenerateInvoiceJob(string orderId) => OrderId = orderId;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Generating invoice for order {OrderId}...");
        // Dynamic chaining: queue another job to run right after this one succeeds.
        context.Then(new SendInvoiceEmailJob(OrderId));
        return Task.CompletedTask;
    }
}

public class SendInvoiceEmailJob : Job
{
    public string OrderId { get; set; } = default!;

    public SendInvoiceEmailJob() { }
    public SendInvoiceEmailJob(string orderId) => OrderId = orderId;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Emailing invoice for order {OrderId}.");
        return Task.CompletedTask;
    }
}

/// <summary>A high-priority job that demonstrates queue priority ordering and explicit queue targeting.</summary>
public class PriorityAlertJob : Job
{
    public string Message { get; set; } = default!;
    public override string Queue => "priority";

    public PriorityAlertJob() { }
    public PriorityAlertJob(string message) => Message = message;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Priority] {Message}");
        return Task.CompletedTask;
    }
}

/// <summary>Demonstrates a failed batch callback once all jobs in a batch have completed.</summary>
public class BatchSummaryJob : Job
{
    public string Summary { get; set; } = default!;
    public override string Queue => "reports";

    public BatchSummaryJob() { }
    public BatchSummaryJob(string summary) => Summary = summary;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Batch summary: {Summary}");
        return Task.CompletedTask;
    }
}
