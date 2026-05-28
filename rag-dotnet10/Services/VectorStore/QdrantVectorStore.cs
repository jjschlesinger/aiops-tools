using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace rag_dotnet10.Services.VectorStore;

public sealed record SearchResult(string Content, string Source, int ChunkIndex, float Score);

/// <summary>
/// Wraps QdrantClient with the vector operations needed by the RAG pipeline.
/// </summary>
public sealed class QdrantVectorStore
{
    private readonly QdrantClient _client;
    private readonly string _collection;
    private readonly uint _dimensions;

    public QdrantVectorStore(QdrantClient client, string collection, int dimensions)
    {
        _client = client;
        _collection = collection;
        _dimensions = (uint)dimensions;
    }

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        bool exists = await _client.CollectionExistsAsync(_collection, cancellationToken);
        if (!exists)
        {
            await _client.CreateCollectionAsync(
                _collection,
                new VectorParams { Size = _dimensions, Distance = Distance.Cosine },
                cancellationToken: cancellationToken);
        }
    }

    public async Task UpsertAsync(
        string id,
        float[] embedding,
        string content,
        string source,
        int chunkIndex,
        CancellationToken cancellationToken = default)
    {
        var point = new PointStruct
        {
            Id = Guid.Parse(id),
            Vectors = embedding,
            Payload =
            {
                ["content"] = content,
                ["source"] = source,
                ["chunk_index"] = (long)chunkIndex
            }
        };

        await _client.UpsertAsync(_collection, [point], cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var results = await _client.SearchAsync(
            _collection,
            queryVector,
            limit: (ulong)topK,
            cancellationToken: cancellationToken);

        return results
            .Select(r => new SearchResult(
                Content: r.Payload["content"].StringValue,
                Source: r.Payload["source"].StringValue,
                ChunkIndex: (int)r.Payload["chunk_index"].IntegerValue,
                Score: r.Score))
            .ToList();
    }
}
