using Naravel.Sample.Config;
using Naravel.Sample.Routes;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("config/appsettings.json", optional: false, reloadOnChange: true)
	.AddEnvironmentVariables();
builder.Services.AddControllers();
QueueSampleConfig.Configure(builder.Services, builder.Configuration);
EventsSampleConfig.Configure(builder.Services);
RoutingSampleConfig.Configure(builder.Services);

var app = builder.Build();

app.UseNaravelRouting();
WebRoutes.Map(app);
app.MapControllers();

app.Run();
