using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace rag_dotnet10.Services.Embedding;

/// <summary>
/// Ollama-backed implementation of IEmbeddingGenerator.
/// Swap this out for any other IEmbeddingGenerator implementation to change the embedding provider.
/// </summary>
public sealed class OllamaEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private bool _disposed;

    public OllamaEmbeddingGenerator(string endpoint, string model, int dimensions)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(endpoint) };
        _model = model;
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();

        var response = await _httpClient.PostAsJsonAsync(
            "/api/embed",
            new OllamaEmbedRequest { Model = _model, Input = inputs },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned null response.");

        return new GeneratedEmbeddings<Embedding<float>>(
            result.Embeddings.Select(e => new Embedding<float>((ReadOnlyMemory<float>)e)));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }

    private sealed class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input")]
        public IList<string> Input { get; set; } = [];
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public float[][] Embeddings { get; set; } = [];
    }
}
