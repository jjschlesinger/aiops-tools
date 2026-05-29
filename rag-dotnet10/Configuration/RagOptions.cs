namespace rag_dotnet10.Configuration;

public sealed class RagOptions
{
    public const string Section = "Rag";
    public string CollectionName { get; set; } = "documents";
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 100;
    public int TopK { get; set; } = 5;
}
