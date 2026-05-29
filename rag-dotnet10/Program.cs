using rag_dotnet10.Configuration;
using rag_dotnet10.Extensions;
using rag_dotnet10.Services.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();

builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.Section));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.Section));
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.Section));

builder.Services.AddGrpc();
builder.Services.AddRag();

var app = builder.Build();

app.MapGrpcService<RagGrpcService>();

await app.RunAsync();
