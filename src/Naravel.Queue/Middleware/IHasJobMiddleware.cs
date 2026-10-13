using Naravel.Queue.Jobs;

namespace Naravel.Queue.Middleware;

/// <summary>
/// Optional contract for a job that declares its own middleware. These run inside the globally registered
/// <see cref="IJobMiddleware"/> instances (PDR-012b). The job creates the instances itself, so nothing is resolved by type name.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>public function middleware()</c> on a job.
/// <b>Why it exists:</b> unique/overlap/rate-limit keys belong to one job, not to every job.
/// <b>Not ported:</b> attribute or reflection discovery.
/// </remarks>
public interface IHasJobMiddleware
{
    /// <summary>The middleware for this execution, outermost first.</summary>
    IEnumerable<IJobMiddleware> Middleware(JobContext context);
}
