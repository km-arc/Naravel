using Microsoft.AspNetCore.Mvc;
using Naravel.Queue.Dispatch;
using Naravel.Sample.Config;

namespace Naravel.Sample.App.Http.Controllers;

[ApiController]
[Route("queue")]
public sealed class QueueDemoController : ControllerBase
{
    private readonly IJobDispatcher _dispatcher;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public QueueDemoController(IJobDispatcher dispatcher, IWebHostEnvironment environment, IConfiguration configuration)
    {
        _dispatcher = dispatcher;
        _environment = environment;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var viewPath = Path.Combine(_environment.ContentRootPath, "resources", "views", "queue", "index.html");
        return PhysicalFile(viewPath, "text/html");
    }

    [HttpPost("dispatch")]
    public async Task<IActionResult> Dispatch()
    {
        await QueueSampleConfig.DispatchDemoJobsAsync(
            _dispatcher,
            _configuration["NaravelQueue:SampleConnection"] ?? "file");
        return Redirect("/queue");
    }
}
