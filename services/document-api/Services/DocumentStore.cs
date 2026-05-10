namespace AiDocumentAssistant.Api.Services;

public sealed class DocumentStore
{
    private readonly Dictionary<Guid, ProcessedDocument> _documents = new();

    public void Add(ProcessedDocument document)
    {
        _documents[document.Id] = document;
    }

    public ProcessedDocument? Get(Guid id)
    {
        return _documents.TryGetValue(id, out var doc) ? doc : null;
    }

    public IReadOnlyCollection<ProcessedDocument> GetAll()
    {
        return _documents.Values.OrderByDescending(x => x.UploadedAt).ToList();
    }
}
