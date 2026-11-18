using Naravel.Events;
using Naravel.Queue.Extensions;
//#if (queue == "file")
using Naravel.Queue.File;
//#elseif (queue == "memory")
using Naravel.Queue.Memory;
//#elseif (queue == "redis")
using Naravel.Queue.Redis;
//#elseif (queue == "rabbitmq")
using Naravel.Queue.RabbitMQ;
//#else
using Naravel.Queue.Kafka;
//#endif
//#if (cache == "redis")
using Naravel.Cache.Redis;
//#endif
using AppRootNamespace.App.Jobs;
using AppRootNamespace.App.Listeners;
using AppRootNamespace.App.Events;
using AppRootNamespace.Bootstrap;
using AppRootNamespace.Routes;

var builder = WebApplication.CreateBuilder(args);

// One JSON file per module under config/. They are read after the defaults ASP.NET Core already loads and before the environment, so
// environment variables, user secrets (Development) and the command line still win. See bootstrap/ConfigurationSetup.cs.
builder.Configuration.AddConfigDirectory(
    Path.Combine(builder.Environment.ContentRootPath, "config"), builder.Environment.IsDevelopment(), args);

//#if (UsesStorage)
// storage/ holds what the application writes while it runs (the file queue, the SQLite file). Git ignores it, so it is created here.
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "storage"));
//#endif

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Services.AddNaravelCache(builder.Configuration);
//#if (cache == "redis")
builder.Services.AddNaravelRedisCache(builder.Configuration);
//#endif
builder.Services.AddNaravelMail(builder.Configuration);
//#if (mail == "smtp")
builder.Services.AddNaravelMailSmtp(builder.Configuration);
//#endif
//#if (database != "none")
builder.Services.AddNaravelDatabase(builder.Configuration);
//#endif
//#if (database == "sqlite")
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);
//#elseif (database == "postgres")
builder.Services.AddNaravelPostgresDatabase(builder.Configuration);
//#elseif (database == "mysql")
builder.Services.AddNaravelMySqlDatabase(builder.Configuration);
//#elseif (database == "sqlserver")
builder.Services.AddNaravelSqlServerDatabase(builder.Configuration);
//#endif

//#if (queue == "file")
builder.Services.AddQueue(builder.Configuration).AddFileDriver(builder.Configuration);
//#elseif (queue == "memory")
builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);
//#elseif (queue == "redis")
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);
//#elseif (queue == "rabbitmq")
builder.Services.AddQueue(builder.Configuration).AddRabbitMqDriver(builder.Configuration);
//#else
builder.Services.AddQueue(builder.Configuration).AddKafkaDriver(builder.Configuration);
//#endif
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
