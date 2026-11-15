using Naravel.Events;
using Naravel.Queue.Extensions;
using Naravel.Queue.File;
using NaravelApp.App.Jobs;
using NaravelApp.App.Listeners;
using NaravelApp.App.Events;
using NaravelApp.Bootstrap;
using NaravelApp.Routes;

var builder = WebApplication.CreateBuilder(args);

// One JSON file per module under config/. They are read after the defaults ASP.NET Core already loads and before the environment, so
// environment variables, user secrets (Development) and the command line still win. See bootstrap/ConfigurationSetup.cs.
builder.Configuration.AddConfigDirectory(
    Path.Combine(builder.Environment.ContentRootPath, "config"), builder.Environment.IsDevelopment(), args);

// storage/ holds what the application writes while it runs (the file queue and the SQLite file). Git ignores it, so it is created here.
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "storage"));

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Services.AddNaravelCache(builder.Configuration);
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelDatabase(builder.Configuration);
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);

builder.Services.AddQueue(builder.Configuration).AddFileDriver(builder.Configuration);
builder.Services.AddQueueWorker();
builder.Services.AddJob<SendWelcomeEmail>("mail.welcome");

builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<UserRegistered>, QueueWelcomeEmail>();

builder.Services.AddNaravelRouting();

var app = builder.Build();

app.UseNaravelRouting();
WebRoutes.Map(app);
app.MapControllers();

app.Run();
