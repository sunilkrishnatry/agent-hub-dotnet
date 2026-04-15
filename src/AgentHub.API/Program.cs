using AgentHub.API;
using AgentHub.API.Routes;

var builder = WebApplication.CreateBuilder(args);

var settings = Settings.Load(builder.Configuration);

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddAgents(settings);

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "AgentHub API");
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapAgentRoutes();

app.Run();
