# AiOps RAG gRPC Service

A headless **.NET 10** Retrieval-Augmented Generation service with a **gRPC bidirectional-streaming** endpoint. AI agents open a persistent stream, send questions, and receive semantically relevant context chunks followed by a Claude-generated answer for each question — all over a single HTTP/2 connection.

---

## Pipeline

```
Question (from agent)
  │
  ▼
Ollama nomic-embed-text         → 768-dim embedding vector
  │
  ▼
Qdrant HNSW ANN search          → top-K nearest document chunks
  │
  ▼
Grounded prompt → Claude API    → answer
  │
  ▼
Stream: ContextChunk × K, then answer  (back to agent)
```

---

## gRPC contract

Defined in `Protos/rag.proto`:

```protobuf
service RagQueryService {
  rpc Query (stream QueryRequest) returns (stream QueryResponse);
}

message QueryRequest {
  string question   = 1;
  string session_id = 2;
}

message QueryResponse {
  string session_id = 1;
  oneof payload {
    ContextChunk context_chunk = 2;
    string       answer        = 3;
  }
}

message ContextChunk {
  string source  = 1;   // document source / filename
  string content = 2;   // chunk text
  float  score   = 3;   // similarity score (0–1)
}
```

For each question the server streams zero-or-more `ContextChunk` messages (one per retrieved document chunk), then a single `answer` message. The client can send the next question without waiting — both sides stream concurrently.

---

## Quick start

### 1. Start Qdrant

Qdrant is the vector store. Run it locally with Docker:

```bash
docker run -d --name qdrant \
  -p 6333:6333 \   # HTTP / REST API
  -p 6334:6334 \   # gRPC (used by this service)
  -v qdrant_storage:/qdrant/storage \
  qdrant/qdrant
```

Qdrant will be available at `localhost:6333` (web UI + REST) and `localhost:6334` (gRPC). The service connects on `6334` by default.

To verify it is running:

```bash
curl http://localhost:6333/healthz
# → {"title":"qdrant - version x.y.z","status":"ok"}
```

### 2. Start Ollama and pull the embedding model

[Ollama](https://ollama.com) serves the embedding model locally. Install it from [ollama.com/download](https://ollama.com/download), then pull the model:

```bash
# Start the Ollama server (runs in the background after install on most platforms)
ollama serve

# Pull the embedding model used by this service
ollama pull nomic-embed-text
```

Verify the model is available:

```bash
ollama list
# nomic-embed-text   ...
```

Ollama listens on `http://localhost:11434` by default — no further configuration needed.

### 3. Configure and run the service

Set your Anthropic API key and start the service:

```bash
# Windows PowerShell
$env:Anthropic__ApiKey = "sk-ant-api03-..."

# Linux / macOS
export Anthropic__ApiKey="sk-ant-api03-..."

cd rag-dotnet10
dotnet restore
dotnet run
# gRPC server listens on http://localhost:5151
```

---

## Configuration

All settings live in `rag-dotnet10/appsettings.json`:

```jsonc
{
  "Rag": {
    "CollectionName": "documents",  // Qdrant collection name
    "ChunkSize": 1000,              // characters per chunk during ingestion
    "ChunkOverlap": 100,            // overlap between adjacent chunks
    "TopK": 5                       // number of chunks to retrieve per question
  },
  "Anthropic": {
    "ApiKey": "",                   // set via environment variable, not here
    "Model": "claude-opus-4-7"
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "EmbeddingModel": "nomic-embed-text",
    "VectorDimensions": 768
  },
  "Qdrant": {
    "Host": "localhost",
    "GrpcPort": 6334,
    "Https": false,
    "ApiKey": null                  // set if using Qdrant Cloud
  },
  "Kestrel": {
    "Endpoints": {
      "Grpc": {
        "Url": "http://localhost:5151",
        "Protocols": "Http2"
      }
    }
  }
}
```

**API key:** always supply `Anthropic__ApiKey` via environment variable or a secrets manager — never commit it to `appsettings.json`.

### Qdrant Cloud

To use [Qdrant Cloud](https://cloud.qdrant.io) instead of a local instance:

```jsonc
"Qdrant": {
  "Host": "your-cluster-id.us-east-1-0.aws.cloud.qdrant.io",
  "GrpcPort": 6334,
  "Https": true,
  "ApiKey": "your-qdrant-api-key"
}
```

---

## Ingesting documents

On startup the service runs `RagHostedService`, which calls `DocumentIngester` to load documents into Qdrant. Place source documents in the configured path (see `DocumentIngester.cs`) before starting the service, or extend the ingestion pipeline to pull from your own source.

The ingestion pipeline:

1. **TextChunker** — splits documents into overlapping chunks (`ChunkSize` / `ChunkOverlap`)
2. **OllamaEmbeddingGenerator** — embeds each chunk with `nomic-embed-text`
3. **QdrantVectorStore** — upserts vectors into the configured collection

---

## Project structure

```
rag-dotnet10/
├── rag-dotnet10.csproj
├── rag-dotnet10.slnx
├── appsettings.json
├── Program.cs                          # host setup, DI wiring, gRPC registration
├── Protos/
│   └── rag.proto                       # gRPC service and message definitions
│
├── Configuration/
│   ├── RagOptions.cs                   # chunk size, top-K, collection name
│   ├── AnthropicOptions.cs             # API key, model
│   ├── OllamaOptions.cs                # endpoint, model, vector dimensions
│   └── QdrantOptions.cs                # host, port, HTTPS, API key
│
├── Models/
│   ├── DocumentChunk.cs                # raw document chunk (source + text)
│   ├── TextChunk.cs                    # chunk with embedding vector
│   └── RagResult.cs                    # retrieved chunk with similarity score
│
├── Pipeline/
│   ├── RagPipeline.cs                  # orchestrates embed → search → generate
│   ├── DocumentIngester.cs             # loads and upserts documents into Qdrant
│   └── TextChunker.cs                  # splits text into overlapping chunks
│
└── Services/
    ├── Embedding/
    │   └── OllamaEmbeddingGenerator.cs # calls Ollama /api/embeddings
    ├── Grpc/
    │   └── RagGrpcService.cs           # gRPC handler — bidirectional stream
    ├── Rag/
    │   ├── RagService.cs               # embed → Qdrant search → Claude call
    │   └── RagHostedService.cs         # IHostedService — runs ingestion on startup
    └── VectorStore/
        └── QdrantVectorStore.cs        # Qdrant upsert and nearest-neighbour search
```

---

## Dependencies

| Package | Purpose |
|---|---|
| `Grpc.AspNetCore` | gRPC server over Kestrel HTTP/2 |
| `Anthropic` SDK | Claude API client |
| `Microsoft.SemanticKernel` | Semantic Kernel connector for Qdrant |
| `Qdrant.Client` | Qdrant gRPC client |
| `Microsoft.Extensions.AI` | Abstractions for embedding generation |

---

## Related

- [`agent/`](../agent/README.md) — the agent that calls this service to enrich its prompts
- [`architecture.svg`](../architecture.svg) — full system interaction diagram
