using AgentHub.API;
using AgentHub.API.Routes;
using AgentHub.Persistence;
using AgentHub.SessionState;

var builder = WebApplication.CreateBuilder(args);

var settings = Settings.Load(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "simple";
});
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
});

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddAgents(settings);
builder.Services.AddSingleton(new PostgresConversationOptions
{
    ConnectionString = settings.PostgresConnectionString
});
builder.Services.AddSingleton<IConversationHistoryRepository, PostgresConversationHistoryRepository>();
builder.Services.AddSingleton<IConversationSessionManager, ConversationSessionManager>();

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.Startup");
startupLogger.LogInformation(
    "Application starting. Environment={Environment}, FoundryEndpoint={FoundryEndpoint}, ModelDeployment={ModelDeployment}, FoundryAgentName={FoundryAgentName}",
    app.Environment.EnvironmentName,
    settings.AzureAIProjectEndpoint,
    settings.AzureAIModelDeploymentName,
    settings.FoundryAgentName ?? AgentHub.API.Agents.FoundryDemoAgent.DefaultName);

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.Request");
    var startedAt = DateTime.UtcNow;

    logger.LogInformation("Request started. Method={Method}, Path={Path}, TraceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.TraceIdentifier);

    await next();

    var durationMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
    logger.LogInformation("Request completed. Method={Method}, Path={Path}, StatusCode={StatusCode}, DurationMs={DurationMs}, TraceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        durationMs,
        context.TraceIdentifier);
});

app.MapHealthChecks("/health");
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "AgentHub API");
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapAgentRoutes();

app.Run();
