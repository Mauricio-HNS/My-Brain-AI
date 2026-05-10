using System.Text.Json;

namespace MyBrainAI.Api.Services;

public sealed class DocumentStore
{
    private readonly object sync = new();
    private readonly string dataFile;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<Guid, ProcessedDocument> documents = new();

    public DocumentStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDirectory);
        dataFile = Path.Combine(dataDirectory, "document-store.json");
        Load();
    }

    public void Add(ProcessedDocument document)
    {
        lock (sync)
        {
            documents[document.Id] = document;
            Save();
        }
    }

    public ProcessedDocument? Get(Guid id)
    {
        lock (sync)
        {
            return documents.TryGetValue(id, out var doc) ? doc : null;
        }
    }

    public IReadOnlyCollection<ProcessedDocument> GetAll()
    {
        lock (sync)
        {
            return documents.Values.OrderByDescending(x => x.UploadedAt).ToList();
        }
    }

    private void Load()
    {
        if (!File.Exists(dataFile))
            return;

        var json = File.ReadAllText(dataFile);
        var savedDocuments = JsonSerializer.Deserialize<List<ProcessedDocument>>(json, jsonOptions) ?? [];
        var migrated = false;

        foreach (var document in savedDocuments)
        {
            var migratedDocument = MigrateBrand(document);
            if (migratedDocument.FileName != document.FileName || ChunksChanged(document.Chunks, migratedDocument.Chunks))
                migrated = true;

            documents[migratedDocument.Id] = migratedDocument;
        }

        if (migrated)
            Save();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(documents.Values.OrderByDescending(x => x.UploadedAt), jsonOptions);
        File.WriteAllText(dataFile, json);
    }

    private static ProcessedDocument MigrateBrand(ProcessedDocument document)
    {
        return new ProcessedDocument
        {
            Id = document.Id,
            FileName = ReplaceOldBrand(document.FileName),
            SizeBytes = document.SizeBytes,
            UploadedAt = document.UploadedAt,
            TotalPages = document.TotalPages,
            TotalTokens = document.TotalTokens,
            Chunks = document.Chunks.Select(chunk => new DocumentChunk
            {
                Index = chunk.Index,
                Page = chunk.Page,
                Text = ReplaceOldBrand(chunk.Text),
                Keywords = chunk.Keywords.Select(ReplaceOldBrand).ToHashSet(StringComparer.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    private static string ReplaceOldBrand(string value)
    {
        return value
            .Replace("NeuroDocs AI", "My Brain AI", StringComparison.OrdinalIgnoreCase)
            .Replace("NeuroDocs-AI", "My-Brain-AI", StringComparison.OrdinalIgnoreCase)
            .Replace("AI Document Assistant", "My Brain AI", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ChunksChanged(List<DocumentChunk> original, List<DocumentChunk> migrated)
    {
        if (original.Count != migrated.Count)
            return true;

        for (var i = 0; i < original.Count; i++)
        {
            if (original[i].Text != migrated[i].Text)
                return true;
        }

        return false;
    }
}
