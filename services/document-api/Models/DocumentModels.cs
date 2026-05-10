namespace AiDocumentAssistant.Api.Services;

public sealed class ProcessedDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FileName { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime UploadedAt { get; init; } = DateTime.UtcNow;
    public int TotalPages { get; init; }
    public int TotalTokens { get; init; }
    public List<DocumentChunk> Chunks { get; init; } = [];

    public object ToSummary() => new
    {
        Id,
        FileName,
        SizeBytes,
        UploadedAt,
        TotalPages,
        TotalChunks = Chunks.Count,
        TotalTokens,
        Status = "Completed"
    };

    public object ToDetail() => new
    {
        Id,
        FileName,
        SizeBytes,
        UploadedAt,
        TotalPages,
        TotalChunks = Chunks.Count,
        TotalTokens,
        Status = "Completed",
        Chunks = Chunks.Take(30)
    };
}

public sealed class DocumentChunk
{
    public int Index { get; init; }
    public int Page { get; init; }
    public string Text { get; init; } = "";
    public HashSet<string> Keywords { get; init; } = [];
}

public sealed class RagAnswer
{
    public string Answer { get; init; } = "";
    public List<SourceReference> Sources { get; init; } = [];
}

public sealed class SourceReference
{
    public int Page { get; init; }
    public int ChunkIndex { get; init; }
    public string Preview { get; init; } = "";
}
