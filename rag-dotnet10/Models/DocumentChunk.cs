using Microsoft.Extensions.VectorData;

namespace rag_dotnet10.Models;

public sealed class DocumentChunk
{
    [VectorStoreKey]
    public Guid Id { get; set; }

    [VectorStoreData]
    public string Content { get; set; } = "";

    [VectorStoreData]
    public string Source { get; set; } = "";

    [VectorStoreData]
    public int ChunkIndex { get; set; }

    // Dimensions must match OllamaOptions.VectorDimensions (768 for nomic-embed-text).
    [VectorStoreVector(768)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
