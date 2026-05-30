using AiOps.Agent.Configuration;
using AiOps.Agent.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
var agentConfig = builder.Configuration
    .GetSection("Agent")
    .Get<AgentConfig>() ?? new AgentConfig();

builder.Services.AddSingleton(agentConfig);

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services
    .AddSingleton<AiOps.Agent.Services.RagClient>(sp =>
    {
        var cfg    = sp.GetRequiredService<AgentConfig>();
        var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AiOps.Agent.Services.RagClient>>();
        return new AiOps.Agent.Services.RagClient(cfg.RagGrpcAddress, logger);
    })
    .AddSingleton<IAgentOrchestrator, AgentOrchestrator>()
    .AddHostedService<LogAnalysisBackgroundService>();

await builder.Build().RunAsync();
