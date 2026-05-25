using AiOps.McpServer.Extensions;
using AiOps.McpServer.Repositories;
using AiOps.McpServer.Services;
using AiOps.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddLogRepositories(builder.Configuration)
    .AddSingleton<ILogRepositoryFactory, LogRepositoryFactory>()
    .AddSingleton<MarkdownReportGenerator>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTool<LogAnalysisTool>();

await builder.Build().RunAsync();
