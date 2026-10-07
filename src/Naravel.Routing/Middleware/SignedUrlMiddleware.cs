using Microsoft.AspNetCore.Http;

namespace Naravel.Routing;

internal sealed class SignedUrlMiddleware(SignedUrlService signedUrls) : IRouteMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        if (!signedUrls.IsValid(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        return next(context);
    }
}