using System.Text.Json;
using AiOps.Agent.Configuration;
using AiOps.Agent.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiOps.Agent.Services;

/// <summary>
/// A long-running <see cref="BackgroundService"/> that periodically invokes
/// <see cref="AgentOrchestrator"/> and writes each <see cref="AnalysisRun"/>
/// result as a dated JSON file in the configured output directory.
/// </summary>
public sealed class LogAnalysisBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
    };

    private readonly AgentConfig _config;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<LogAnalysisBackgroundService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public LogAnalysisBackgroundService(
        AgentConfig config,
        IAgentOrchestrator orchestrator,
        ILogger<LogAnalysisBackgroundService> logger,
        IHostApplicationLifetime lifetime)
    {
        _config = config;
        _orchestrator = orchestrator;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure output directory exists.
        Directory.CreateDirectory(_config.OutputDirectory);

        _logger.LogInformation(
            "Log analysis agent started. Model={Model}, Interval={Minutes}min, Output={Dir}",
            _config.Model, _config.IntervalMinutes, Path.GetFullPath(_config.OutputDirectory));

        // Run immediately on startup, then on the configured interval.
        do
        {
            await RunOnceAsync(stoppingToken);

            if (_config.IntervalMinutes <= 0)
            {
                // One-shot mode: stop after the first run.
                _logger.LogInformation("One-shot mode — stopping host.");
                _lifetime.StopApplication();
                break;
            }

            _logger.LogInformation("Next run in {Minutes} minutes.", _config.IntervalMinutes);
            await Task.Delay(TimeSpan.FromMinutes(_config.IntervalMinutes), stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        _logger.LogInformation("=== Starting analysis run ===");

        var run = await _orchestrator.RunAsync(ct);

        var outputPath = BuildOutputPath(run);

        await WriteRunAsync(run, outputPath, ct);

        if (run.Success)
        {
            _logger.LogInformation(
                "=== Run completed successfully. Tokens: {In}/{Out} in, {ToolCount} tool calls. File: {Path} ===",
                run.InputTokens, run.OutputTokens, run.ToolCalls.Count, outputPath);
        }
        else
        {
            _logger.LogWarning(
                "=== Run FAILED: {ErrType} — {ErrMsg}. File: {Path} ===",
                run.ErrorType, run.ErrorMessage, outputPath);
        }
    }

    private string BuildOutputPath(AnalysisRun run)
    {
        var timestamp = run.StartedAt.ToString("yyyyMMdd_HHmmss");
        var filename = $"analysis_{timestamp}_{run.RunId[..8]}.json";
        return Path.Combine(_config.OutputDirectory, filename);
    }

    private static async Task WriteRunAsync(AnalysisRun run, string path, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(run, _writeOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}
