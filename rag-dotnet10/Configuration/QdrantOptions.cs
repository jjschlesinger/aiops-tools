namespace rag_dotnet10.Configuration;

public sealed class QdrantOptions
{
    public const string Section = "Qdrant";
    public string Host { get; set; } = "localhost";
    public int GrpcPort { get; set; } = 6334;
    public bool Https { get; set; } = false;
    public string? ApiKey { get; set; }
}
