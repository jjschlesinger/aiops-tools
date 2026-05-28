namespace rag_dotnet10.Configuration;

public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>
    /// Anthropic API key. If empty, falls back to the ANTHROPIC_API_KEY environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-opus-4-7";
}
