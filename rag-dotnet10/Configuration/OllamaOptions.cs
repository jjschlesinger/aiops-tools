namespace rag_dotnet10.Configuration;

public sealed class OllamaOptions
{
    public const string Section = "Ollama";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int VectorDimensions { get; set; } = 768;
}
