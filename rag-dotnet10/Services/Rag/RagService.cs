using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using rag_dotnet10.Models;

namespace rag_dotnet10.Services.Rag;

public sealed class RagService(Kernel kernel, VectorStoreCollection<Guid, DocumentChunk> collection)
{
    public async Task<RagResult> QueryAsync(string userQuestion, CancellationToken cancellationToken = default)
    {
        var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var queryEmbeddings = await embeddingGenerator.GenerateAsync([userQuestion], cancellationToken: cancellationToken);
        var queryEmbedding = queryEmbeddings[0].Vector;

        var retrievedChunks = new List<RetrievedChunk>();
        await foreach (var result in collection.SearchAsync(queryEmbedding, top: 5, cancellationToken: cancellationToken))
        {
            retrievedChunks.Add(new RetrievedChunk(
                result.Record.Source,
                result.Record.Content,
                (float)(result.Score ?? 0)));
        }

        if (retrievedChunks.Count == 0)
            return new RagResult("No relevant information found in the knowledge base.", []);

        var context = string.Join("\n\n---\n\n",
            retrievedChunks.Select(c => $"[Source: {c.Source}]\n{c.Content}"));

        var prompt = $"""
            You are a helpful assistant. Answer the question using ONLY the context below.
            If the answer isn't in the context, say "I don't know."

            ## Context
            {context}

            ## Question
            {userQuestion}
            """;

        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatService.GetChatMessageContentAsync(
            prompt,
            cancellationToken: cancellationToken);

        return new RagResult(response.Content ?? "No response generated.", retrievedChunks);
    }
}
