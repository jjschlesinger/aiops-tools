using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using rag_dotnet10.Models;
using rag_dotnet10.Pipeline;

namespace rag_dotnet10.Services.Rag;

public sealed class RagHostedService(
    DocumentIngester ingester,
    VectorStoreCollection<Guid, DocumentChunk> collection,
    ILogger<RagHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await collection.EnsureCollectionExistsAsync(stoppingToken);

        logger.LogInformation("Ingesting sample documents...");
        await IngestSampleDocumentsAsync(stoppingToken);
        logger.LogInformation("Ingestion complete. gRPC server is ready.");
    }

    private async Task IngestSampleDocumentsAsync(CancellationToken cancellationToken)
    {
        await ingester.IngestAsync(
            source: "dotnet-overview.txt",
            content: """
            .NET is a free, open-source, cross-platform developer platform built by Microsoft. It supports
            multiple programming languages including C#, F#, and Visual Basic. .NET 10 is the latest version,
            delivering performance improvements, new C# language features, and enhanced AI tooling through
            Microsoft.Extensions.AI. The framework targets web, desktop, mobile, cloud, IoT, and AI workloads.
            """,
            cancellationToken: cancellationToken);

        await ingester.IngestAsync(
            source: "rag-overview.txt",
            content: """
            Retrieval-Augmented Generation (RAG) is an AI pattern that combines a retrieval step with language
            model generation. Rather than relying solely on the model's training data, RAG first retrieves
            relevant documents from an external knowledge base using semantic (vector) search, then passes
            those documents as context to the language model. This grounds responses in specific, up-to-date
            information and reduces hallucination. The main components are: a vector store (e.g. Qdrant),
            an embedding model to encode text as dense vectors, and a language model (e.g. Claude) for generation.
            """,
            cancellationToken: cancellationToken);

        await ingester.IngestAsync(
            source: "qdrant-overview.txt",
            content: """
            Qdrant is an open-source vector database and similarity search engine written in Rust. It stores
            high-dimensional embedding vectors alongside arbitrary JSON payloads and supports cosine, dot-product,
            and Euclidean distance metrics. Qdrant exposes both a REST HTTP API and a gRPC API. The .NET client
            library communicates over gRPC by default on port 6334. Collections hold points, each consisting of
            an ID, a dense vector, and an optional payload. Approximate nearest-neighbour search is powered by
            the HNSW algorithm, giving sub-millisecond latency at scale.
            """,
            cancellationToken: cancellationToken);
    }
}
