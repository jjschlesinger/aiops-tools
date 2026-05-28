using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Qdrant.Client;
using rag_dotnet10.Configuration;
using rag_dotnet10.Models;
using rag_dotnet10.Pipeline;
using rag_dotnet10.Services.Embedding;
using rag_dotnet10.Services.Rag;

namespace rag_dotnet10.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the full RAG stack into the host's DI container.
    ///
    /// Inference swap point  : replace AddAnthropicInference with any registration
    ///                         that provides IChatCompletionService.
    /// Embedding swap point  : replace AddOllamaEmbedding with any registration
    ///                         that provides ITextEmbeddingGenerationService.
    /// Vector store swap point: replace AddQdrantCollection with any registration
    ///                         that provides VectorStoreCollection&lt;Guid, DocumentChunk&gt;.
    /// </summary>
    public static IServiceCollection AddRag(this IServiceCollection services)
    {
        services
            .AddAnthropicInference()
            .AddOllamaEmbedding()
            .AddQdrantCollection()
            .AddKernel()
            .AddPipeline();

        return services;
    }

    // ── Inference ────────────────────────────────────────────────────────────

    private static IServiceCollection AddAnthropicInference(this IServiceCollection services)
    {
        // The Anthropic SDK reads ANTHROPIC_API_KEY from the environment by default.
        // If the config supplies a key it is forwarded here so both paths work.
        services.AddSingleton<IChatClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", opts.ApiKey);
            return new AnthropicClient().AsIChatClient(opts.Model);
        });

        // Bridge MEAI IChatClient → SK IChatCompletionService.
        services.AddSingleton<IChatCompletionService>(sp =>
            sp.GetRequiredService<IChatClient>().AsChatCompletionService(sp));

        return services;
    }

    // ── Embeddings ───────────────────────────────────────────────────────────

    private static IServiceCollection AddOllamaEmbedding(this IServiceCollection services)
    {
        // MEAI IEmbeddingGenerator — the concrete Ollama implementation.
        // The Kernel resolves this directly; no SK bridge needed since
        // ITextEmbeddingGenerationService is now obsolete in favour of MEAI.
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaEmbeddingGenerator(opts.Endpoint, opts.EmbeddingModel, opts.VectorDimensions);
        });

        return services;
    }

    // ── Vector store ─────────────────────────────────────────────────────────

    private static IServiceCollection AddQdrantCollection(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(opts.Host, opts.GrpcPort, https: opts.Https, apiKey: opts.ApiKey);
        });

        services.AddSingleton<VectorStoreCollection<Guid, DocumentChunk>>(sp =>
        {
            var qdrantClient = sp.GetRequiredService<QdrantClient>();
            var ragOpts = sp.GetRequiredService<IOptions<RagOptions>>().Value;
            // positional: (client, collectionName, hasNamedVectors, options)
            return new QdrantCollection<Guid, DocumentChunk>(qdrantClient, ragOpts.CollectionName, false, null);
        });

        return services;
    }

    // ── Semantic Kernel ───────────────────────────────────────────────────────

    private static IServiceCollection AddKernel(this IServiceCollection services)
    {
        // Build the Kernel from the host's DI container so it resolves
        // IChatCompletionService and ITextEmbeddingGenerationService from above.
        services.AddTransient<Kernel>(sp => new Kernel(sp));
        return services;
    }

    // ── Pipeline ─────────────────────────────────────────────────────────────

    private static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RagOptions>>().Value;
            return new TextChunker(opts.ChunkSize, opts.ChunkOverlap);
        });

        services.AddSingleton<DocumentIngester>();
        services.AddSingleton<RagService>();
        services.AddHostedService<RagHostedService>();

        return services;
    }
}
