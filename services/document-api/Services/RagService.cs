using System.Text.RegularExpressions;

namespace AiDocumentAssistant.Api.Services;

public sealed class RagService
{
    private static readonly Regex WordRegex = new("[a-zA-ZÀ-ÿ0-9]{3,}", RegexOptions.Compiled);

    public Task<RagAnswer> AnswerAsync(ProcessedDocument document, string question)
    {
        var queryTerms = ExtractTerms(question);

        var topChunks = document.Chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = chunk.Keywords.Intersect(queryTerms).Count()
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.Index)
            .Take(5)
            .Select(x => x.Chunk)
            .ToList();

        if (topChunks.Count == 0)
        {
            return Task.FromResult(new RagAnswer
            {
                Answer = "I could not find enough relevant context in the uploaded document to answer this question.",
                Sources = []
            });
        }

        var context = string.Join("\n\n", topChunks.Select(x => $"Page {x.Page}: {x.Text}"));

        // Production upgrade:
        // Replace this local answer builder with Azure OpenAI/OpenAI Chat Completions.
        // Send: system prompt + question + retrieved context.
        var answer =
            "Based on the retrieved document sections, the most relevant information is:\n\n" +
            BuildExtractiveAnswer(question, topChunks);

        return Task.FromResult(new RagAnswer
        {
            Answer = answer,
            Sources = topChunks.Select(x => new SourceReference
            {
                Page = x.Page,
                ChunkIndex = x.Index,
                Preview = x.Text.Length > 220 ? x.Text[..220] + "..." : x.Text
            }).ToList()
        });
    }

    private static string BuildExtractiveAnswer(string question, List<DocumentChunk> chunks)
    {
        var sentences = chunks
            .SelectMany(c => c.Text.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => new { c.Page, Text = s.Trim() }))
            .Where(x => x.Text.Length > 40)
            .Take(6)
            .Select(x => $"- {x.Text}. [page {x.Page}]");

        return string.Join("\n", sentences);
    }

    private static HashSet<string> ExtractTerms(string text)
    {
        return WordRegex.Matches(text.ToLowerInvariant())
            .Select(x => x.Value)
            .Where(x => x.Length > 3)
            .ToHashSet();
    }
}
