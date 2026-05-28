using rag_dotnet10.Models;

namespace rag_dotnet10.Pipeline;

public sealed class TextChunker
{
    private readonly int _chunkSize;
    private readonly int _overlap;
    private readonly int _step;

    public TextChunker(int chunkSize, int overlap)
    {
        _chunkSize = chunkSize;
        _overlap = Math.Min(overlap, chunkSize - 1);
        _step = _chunkSize - _overlap;
    }

    public IEnumerable<TextChunk> Chunk(string source, string content)
    {
        var text = content.ReplaceLineEndings(" ").Trim();
        int pos = 0;
        int chunkIndex = 0;

        while (pos < text.Length)
        {
            int end = Math.Min(pos + _chunkSize, text.Length);

            // Snap to a word boundary to avoid cutting mid-word.
            if (end < text.Length)
            {
                int snap = text.LastIndexOf(' ', end - 1);
                if (snap > pos) end = snap;
            }

            var chunk = text[pos..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return new TextChunk
                {
                    Id = Guid.NewGuid(),
                    Content = chunk,
                    Source = source,
                    ChunkIndex = chunkIndex++
                };
            }

            pos += _step;
        }
    }
}
