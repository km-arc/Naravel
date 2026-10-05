using Naravel.Queue.Jobs;

namespace Naravel.Queue.Middleware;

/// <summary>
/// Wraps job execution, similar to ASP.NET Core middleware or Laravel's job middleware
/// (RateLimited, WithoutOverlapping, ThrottlesExceptions, ...). Register any number of these
/// in DI (services.AddSingleton&lt;IJobMiddleware, MyMiddleware&gt;()) and they'll run, in registration
/// order, around every job on every queue.
/// </summary>
public interface IJobMiddleware
{
    Task InvokeAsync(JobContext context, Func<Task> next);
}
