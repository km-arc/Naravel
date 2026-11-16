using Microsoft.AspNetCore.Mvc;
using Naravel.Events;
using Naravel.Events.Testing;
using Naravel.Mail.Testing;
using Naravel.Queue.Testing;
using AppRootNamespace.App.Events;
using AppRootNamespace.App.Http.Controllers;
using AppRootNamespace.App.Http.Requests;
using AppRootNamespace.App.Jobs;
using AppRootNamespace.App.Listeners;
using AppRootNamespace.App.Mail;

namespace AppRootNamespace.Tests;

// The three steps of the example flow, each against a fake, so no queue and no mail server is needed.
public sealed class RegistrationFlowTests
{
    [Fact]
    public async Task The_controller_dispatches_UserRegistered()
    {
        var events = new EventFake();
        var controller = new RegistrationController(events);

        var result = await controller.Register(new RegisterRequest("ada@example.com", "Ada"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var dispatched = events.AssertDispatched<UserRegistered>();
        Assert.Equal("ada@example.com", dispatched.Email);
        Assert.Equal("Ada", dispatched.Name);
    }

    [Fact]
    public async Task The_listener_queues_the_welcome_job()
    {
        var queue = new QueueFake();
        var listener = new QueueWelcomeEmail(queue);

        await listener.HandleAsync(new UserRegistered("ada@example.com", "Ada"), new EventDispatchContext());

        var job = queue.AssertDispatched<SendWelcomeEmail>();
        Assert.Equal("ada@example.com", job.Email);
        Assert.Equal("Ada", job.Name);
    }

    [Fact]
    public async Task The_welcome_mail_is_addressed_and_worded()
    {
        var mail = new MailFake { DefaultFrom = "hello@example.com" };

        await mail.To("ada@example.com").SendAsync(new WelcomeMail("Ada"));

        var message = mail.AssertSent();
        Assert.Equal("hello@example.com", message.From);
        Assert.Equal("ada@example.com", Assert.Single(message.To));
        Assert.Equal("Welcome", message.Subject);
        Assert.Contains("Ada", message.TextBody);
    }

    [Fact]
    public async Task The_html_body_escapes_the_name()
    {
        var mail = new MailFake { DefaultFrom = "hello@example.com" };

        await mail.To("eve@example.com").SendAsync(new WelcomeMail("<b>Eve</b>"));

        var html = mail.AssertSent().HtmlBody;
        Assert.NotNull(html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;Eve&lt;/b&gt;", html);
    }
}
