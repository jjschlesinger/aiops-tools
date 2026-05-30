using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Rag.Protos;

namespace AiOps.Agent.Services;

/// <summary>
/// Wraps the RAG gRPC bidirectional-streaming channel.
/// Each <see cref="QueryAsync"/> call opens one bidi stream, sends a single
/// question, and collects all response payloads (context chunks + final answer)
/// before closing the client-side of the stream.
/// </summary>
public sealed class RagClient : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly RagQueryService.RagQueryServiceClient _grpcClient;
    private readonly ILogger<RagClient> _logger;

    public RagClient(string address, ILogger<RagClient> logger)
    {
        _logger = logger;
        _channel = GrpcChannel.ForAddress(address);
        _grpcClient = new RagQueryService.RagQueryServiceClient(_channel);
        _logger.LogInformation("RAG gRPC channel opened → {Address}", address);
    }

    /// <summary>
    /// Sends <paramref name="question"/> over the bidi stream and asynchronously
    /// yields each <see cref="QueryResponse"/> as it arrives from the server.
    /// </summary>
    public async IAsyncEnumerable<QueryResponse> QueryAsync(
        string question,
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var call = _grpcClient.Query(cancellationToken: ct);

        // Send the single request then signal end-of-stream from the client side.
        await call.RequestStream.WriteAsync(
            new QueryRequest { Question = question, SessionId = sessionId }, ct);
        await call.RequestStream.CompleteAsync();

        _logger.LogDebug("RAG query sent (session={Session}): {Question}", sessionId, question);

        await foreach (var response in call.ResponseStream.ReadAllAsync(ct))
        {
            _logger.LogDebug("RAG response (session={Session}, payload={Kind})",
                response.SessionId, response.PayloadCase);
            yield return response;
        }
    }

    /// <summary>
    /// Convenience overload that accumulates the full answer text and returns
    /// it together with the received context chunks.
    /// </summary>
    public async Task<RagResult> QueryFullAsync(
        string question,
        string sessionId,
        CancellationToken ct = default)
    {
        var chunks = new List<ContextChunk>();
        var answerBuilder = new System.Text.StringBuilder();

        await foreach (var response in QueryAsync(question, sessionId, ct))
        {
            switch (response.PayloadCase)
            {
                case QueryResponse.PayloadOneofCase.ContextChunk:
                    chunks.Add(response.ContextChunk);
                    break;
                case QueryResponse.PayloadOneofCase.Answer:
                    answerBuilder.Append(response.Answer);
                    break;
            }
        }

        return new RagResult(answerBuilder.ToString(), chunks);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.ShutdownAsync();
        _channel.Dispose();
    }
}

/// <param name="Answer">The final answer assembled from all answer payloads.</param>
/// <param name="ContextChunks">Ordered list of context chunks returned before the answer.</param>
public sealed record RagResult(string Answer, IReadOnlyList<ContextChunk> ContextChunks);
