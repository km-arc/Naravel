using Microsoft.AspNetCore.Mvc;
using Naravel.Events;
using NaravelApp.App.Events;
using NaravelApp.App.Http.Requests;

namespace NaravelApp.App.Http.Controllers;

/// <summary>
/// An example controller. It does one thing: it announces that a user registered. What happens next (a welcome mail) is the business of
/// the listeners, so this class does not change when that grows.
/// </summary>
[ApiController]
public sealed class RegistrationController(IEventDispatcher events) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        await events.DispatchAsync(new UserRegistered(request.Email, request.Name), cancellationToken);

        // 202: the work continues in the queue worker, after the response.
        return Accepted();
    }
}
