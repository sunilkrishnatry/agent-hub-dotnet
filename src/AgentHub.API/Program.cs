using AgentHub.API;
using AgentHub.API.Routes;

var builder = WebApplication.CreateBuilder(args);

var settings = Settings.Load(builder.Configuration);

builder.Services.AddHealthChecks();
builder.Services.AddAgents(settings);

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapAgentRoutes();

app.Run();
