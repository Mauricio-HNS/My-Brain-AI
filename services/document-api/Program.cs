using AiDocumentAssistant.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", p =>
        p.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod());
});

builder.Services.AddSingleton<DocumentStore>();
builder.Services.AddSingleton<KnowledgeStore>();
builder.Services.AddSingleton<DocumentProcessor>();
builder.Services.AddSingleton<RagService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("Frontend");

app.MapGet("/", () => Results.Ok(new { name = "My Brain AI API", status = "running" }));

app.MapPost("/api/documents/upload", async (
    IFormFile file,
    DocumentProcessor processor,
    DocumentStore store,
    KnowledgeStore knowledge) =>
{
    if (file.Length == 0)
        return Results.BadRequest("Empty file.");

    var doc = await processor.ProcessAsync(file);
    store.Add(doc);
    knowledge.AddDocument(doc);

    return Results.Ok(doc.ToSummary());
})
.DisableAntiforgery();

app.MapGet("/api/documents", (DocumentStore store) =>
{
    return Results.Ok(store.GetAll().Select(x => x.ToSummary()));
});

app.MapGet("/api/documents/{id:guid}", (Guid id, DocumentStore store) =>
{
    var doc = store.Get(id);
    return doc is null ? Results.NotFound() : Results.Ok(doc.ToDetail());
});

app.MapPost("/api/chat", async (ChatRequest request, RagService rag, DocumentStore store) =>
{
    var doc = store.Get(request.DocumentId);
    if (doc is null)
        return Results.NotFound("Document not found.");

    var result = await rag.AnswerAsync(doc, request.Question);
    return Results.Ok(result);
});

app.MapGet("/api/knowledge", (KnowledgeStore knowledge) =>
{
    return Results.Ok(knowledge.GetAll().Take(100));
});

app.MapPost("/api/knowledge/teach", (TeachRequest request, KnowledgeStore knowledge) =>
{
    if (string.IsNullOrWhiteSpace(request.Content))
        return Results.BadRequest("Content is required.");

    var entry = knowledge.AddMemory(request.Title, request.Content, request.Tags);
    return Results.Ok(entry);
});

app.MapPost("/api/knowledge/ask", (KnowledgeAskRequest request, KnowledgeStore knowledge) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest("Question is required.");

    return Results.Ok(knowledge.Ask(request.Question, request.DocumentId));
});

app.MapPost("/api/knowledge/feedback", (FeedbackRequest request, KnowledgeStore knowledge) =>
{
    if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.CorrectAnswer))
        return Results.BadRequest("Question and correct answer are required.");

    var entry = knowledge.AddCorrection(request.Question, request.CorrectAnswer, request.DocumentId);
    return Results.Ok(entry);
});

app.Run();

public record ChatRequest(Guid DocumentId, string Question);
