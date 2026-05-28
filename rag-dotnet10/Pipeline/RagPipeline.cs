using Microsoft.Extensions.AI;
using rag_dotnet10.Services.VectorStore;

namespace rag_dotnet10.Pipeline;

public sealed class RagPipeline
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    private readonly QdrantVectorStore _vectorStore;
    private readonly IChatClient _chatClient;
    private readonly int _topK;

    public RagPipeline(
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        QdrantVectorStore vectorStore,
        IChatClient chatClient,
        int topK)
    {
        _embedder = embedder;
        _vectorStore = vectorStore;
        _chatClient = chatClient;
        _topK = topK;
    }

    public async Task<string> QueryAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embedder.GenerateAsync([question], cancellationToken: cancellationToken);
        var queryVector = queryEmbedding[0].Vector.ToArray();

        var chunks = await _vectorStore.SearchAsync(queryVector, _topK, cancellationToken);
        if (chunks.Count == 0)
            return "No relevant information found in the knowledge base.";

        var context = string.Join("\n\n", chunks.Select(c =>
            $"[Source: {c.Source}, relevance: {c.Score:P0}]\n{c.Content}"));

        var messages = new ChatMessage[]
        {
            new(ChatRole.System,
                "You are a helpful assistant. Answer questions using only the provided context. " +
                "If the context is insufficient, say so clearly."),
            new(ChatRole.User, $"Context:\n{context}\n\nQuestion: {question}")
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }
}
