using System.Text.Json;
using AiOps.Agent.Services;
using ModelContextProtocol.Protocol.Types;

namespace AiOps.Agent.Tests.Services;

/// <summary>
/// Unit tests for the <c>internal static</c> helper methods on
/// <see cref="AgentOrchestrator"/> exposed via <c>InternalsVisibleTo</c>.
/// These are pure functions with no I/O that are easy to test exhaustively.
/// </summary>
public sealed class AgentOrchestratorHelpersTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // BuildToolArgs — converts JsonElement values to CLR types
    // ─────────────────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, JsonElement> Json(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void BuildToolArgs_StringValue_ReturnsString()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"key":"hello"}"""));
        args["key"].Should().BeOfType<string>().Which.Should().Be("hello");
    }

    [Fact]
    public void BuildToolArgs_NullJsonString_ReturnsEmptyString()
    {
        // JsonElement string that is actually null JSON
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"key":null}"""));
        args["key"].Should().BeOfType<string>().Which.Should().BeEmpty();
    }

    [Fact]
    public void BuildToolArgs_IntegerValue_ReturnsLong()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"count":42}"""));
        args["count"].Should().BeOfType<long>().Which.Should().Be(42L);
    }

    [Fact]
    public void BuildToolArgs_LargeInteger_ReturnsLong()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"v":9876543210}"""));
        args["v"].Should().BeOfType<long>().Which.Should().Be(9876543210L);
    }

    [Fact]
    public void BuildToolArgs_FloatValue_ReturnsDouble()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"ratio":3.14}"""));
        args["ratio"].Should().BeOfType<double>().Which.Should().BeApproximately(3.14, 0.0001);
    }

    [Fact]
    public void BuildToolArgs_TrueValue_ReturnsTrue()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"flag":true}"""));
        args["flag"].Should().BeOfType<bool>().Which.Should().BeTrue();
    }

    [Fact]
    public void BuildToolArgs_FalseValue_ReturnsFalse()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"flag":false}"""));
        args["flag"].Should().BeOfType<bool>().Which.Should().BeFalse();
    }

    [Fact]
    public void BuildToolArgs_ObjectValue_ReturnsRawJsonString()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"nested":{"a":1}}"""));
        args["nested"].Should().BeOfType<string>()
            .Which.Should().Contain("\"a\"");
    }

    [Fact]
    public void BuildToolArgs_ArrayValue_ReturnsRawJsonString()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("""{"items":[1,2,3]}"""));
        args["items"].Should().BeOfType<string>()
            .Which.Should().StartWith("[");
    }

    [Fact]
    public void BuildToolArgs_EmptyInput_ReturnsEmptyDictionary()
    {
        var args = AgentOrchestrator.BuildToolArgs(Json("{}"));
        args.Should().BeEmpty();
    }

    [Fact]
    public void BuildToolArgs_MultipleKeys_PreservesAllEntries()
    {
        var args = AgentOrchestrator.BuildToolArgs(
            Json("""{"a":"x","b":1,"c":true}"""));

        args.Should().HaveCount(3);
        args["a"].Should().BeOfType<string>();
        args["b"].Should().BeOfType<long>();
        args["c"].Should().BeOfType<bool>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ExtractText — pulls text from a CallToolResponse
    // ─────────────────────────────────────────────────────────────────────────

    private static Content TextContent(string text) =>
        new() { Type = "text", Text = text };

    [Fact]
    public void ExtractText_NullContent_ReturnsNoOutputPlaceholder()
    {
        var result = new CallToolResponse { Content = null! };
        AgentOrchestrator.ExtractText(result).Should().Be("(no output)");
    }

    [Fact]
    public void ExtractText_EmptyContent_ReturnsNoOutputPlaceholder()
    {
        var result = new CallToolResponse { Content = [] };
        AgentOrchestrator.ExtractText(result).Should().Be("(no output)");
    }

    [Fact]
    public void ExtractText_SingleTextContent_ReturnsText()
    {
        var result = new CallToolResponse
        {
            Content = [TextContent("Hello, world!")]
        };
        AgentOrchestrator.ExtractText(result).Should().Be("Hello, world!");
    }

    [Fact]
    public void ExtractText_MultipleTextContent_JoinsWithNewline()
    {
        var result = new CallToolResponse
        {
            Content = [TextContent("Line 1"), TextContent("Line 2")]
        };
        AgentOrchestrator.ExtractText(result).Should().Be("Line 1\nLine 2");
    }

    [Fact]
    public void ExtractText_ContentWithNullText_FallsBackToType()
    {
        var result = new CallToolResponse
        {
            Content = [new Content { Type = "image", Text = null! }]
        };
        // Should return the type string since Text is null
        AgentOrchestrator.ExtractText(result).Should().Be("image");
    }

    [Fact]
    public void ExtractText_ContentWithBothNullTextAndType_IsFiltered()
    {
        var result = new CallToolResponse
        {
            Content = [new Content { Type = null!, Text = null! }, TextContent("real")]
        };
        // The null/null entry is filtered; only "real" remains
        AgentOrchestrator.ExtractText(result).Should().Be("real");
    }

    [Fact]
    public void ExtractText_WhitespaceOnlyContent_IsFiltered()
    {
        var result = new CallToolResponse
        {
            Content = [TextContent("   "), TextContent("data")]
        };
        AgentOrchestrator.ExtractText(result).Should().Be("data");
    }
}
