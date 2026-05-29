using Grpc.Core;
using Rag.Protos;
using rag_dotnet10.Models;
using rag_dotnet10.Services.Rag;

namespace rag_dotnet10.Services.Grpc;

public sealed class RagGrpcService(RagService ragService, ILogger<RagGrpcService> logger)
    : RagQueryService.RagQueryServiceBase
{
    public override async Task Query(
        IAsyncStreamReader<QueryRequest> requestStream,
        IServerStreamWriter<QueryResponse> responseStream,
        ServerCallContext context)
    {
        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            logger.LogDebug("Query received — session={SessionId} question={Question}",
                request.SessionId, request.Question);

            RagResult result;
            try
            {
                result = await ragService.QueryAsync(request.Question, context.CancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing query for session {SessionId}", request.SessionId);
                throw new RpcException(new Status(StatusCode.Internal, "Error processing query"));
            }

            foreach (var chunk in result.Chunks)
            {
                await responseStream.WriteAsync(new QueryResponse
                {
                    SessionId = request.SessionId,
                    ContextChunk = new ContextChunk
                    {
                        Source  = chunk.Source,
                        Content = chunk.Content,
                        Score   = chunk.Score
                    }
                }, context.CancellationToken);
            }

            await responseStream.WriteAsync(new QueryResponse
            {
                SessionId = request.SessionId,
                Answer    = result.Answer
            }, context.CancellationToken);
        }
    }
}
