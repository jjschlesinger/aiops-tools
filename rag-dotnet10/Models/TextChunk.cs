namespace rag_dotnet10.Models;

public sealed record TextChunk
{
    public required Guid Id { get; init; }
    public required string Content { get; init; }
    public required string Source { get; init; }
    public required int ChunkIndex { get; init; }
}
