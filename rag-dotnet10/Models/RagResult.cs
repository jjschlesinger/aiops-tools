namespace rag_dotnet10.Models;

public sealed record RagResult(string Answer, IReadOnlyList<RetrievedChunk> Chunks);

public sealed record RetrievedChunk(string Source, string Content, float Score);
