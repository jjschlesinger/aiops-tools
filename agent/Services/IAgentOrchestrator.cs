using AiOps.Agent.Models;

namespace AiOps.Agent.Services;

/// <summary>
/// Abstraction over a single analysis run, allowing the background service
/// to be unit-tested independently of the Anthropic and MCP infrastructure.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Runs one full agentic analysis pass and returns the completed run record.
    /// This method never throws; failures are captured inside the returned run.
    /// </summary>
    Task<AnalysisRun> RunAsync(CancellationToken cancellationToken = default);
}
