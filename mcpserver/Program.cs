using AiOps.McpServer.Configuration;
using AiOps.McpServer.Repositories;
using AiOps.McpServer.Services;
using AiOps.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .Configure<LogRepositorySettings>(
        builder.Configuration.GetSection("LogRepositories"))
    .AddSingleton<ILogRepositoryFactory, LogRepositoryFactory>()
    .AddSingleton<MarkdownReportGenerator>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LogAnalysisTool>();

await builder.Build().RunAsync();
