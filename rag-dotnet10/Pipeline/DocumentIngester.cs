using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using rag_dotnet10.Models;

namespace rag_dotnet10.Pipeline;

public sealed class DocumentIngester(
    TextChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    VectorStoreCollection<Guid, DocumentChunk> collection)
{
    public async Task IngestAsync(
        string source,
        string content,
        CancellationToken cancellationToken = default)
    {
        var chunks = chunker.Chunk(source, content).ToList();
        if (chunks.Count == 0) return;

        var embeddings = await embeddingGenerator.GenerateAsync(
            chunks.Select(c => c.Content),
            cancellationToken: cancellationToken);

        var documents = chunks.Select((chunk, i) => new DocumentChunk
        {
            Id = chunk.Id,
            Content = chunk.Content,
            Source = chunk.Source,
            ChunkIndex = chunk.ChunkIndex,
            Embedding = embeddings[i].Vector
        });

        await collection.UpsertAsync(documents, cancellationToken);
    }
}
