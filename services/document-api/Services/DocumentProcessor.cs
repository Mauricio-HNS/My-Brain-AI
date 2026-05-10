using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace AiDocumentAssistant.Api.Services;

public sealed class DocumentProcessor
{
    private static readonly Regex WordRegex = new("[a-zA-ZÀ-ÿ0-9]{3,}", RegexOptions.Compiled);

    public async Task<ProcessedDocument> ProcessAsync(IFormFile file)
    {
        var tempFile = Path.GetTempFileName();

        await using (var stream = File.Create(tempFile))
        {
            await file.CopyToAsync(stream);
        }

        try
        {
            var pages = ExtractTextByPage(tempFile, file.FileName);
            var chunks = CreateChunks(pages, 900);

            return new ProcessedDocument
            {
                FileName = file.FileName,
                SizeBytes = file.Length,
                TotalPages = pages.Count,
                TotalTokens = chunks.Sum(c => c.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
                Chunks = chunks
            };
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static List<(int Page, string Text)> ExtractTextByPage(string path, string fileName)
    {
        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var document = PdfDocument.Open(path);
            return document.GetPages()
                .Select(p => (p.Number, Clean(p.Text)))
                .Where(p => !string.IsNullOrWhiteSpace(p.Item2))
                .ToList();
        }

        var text = File.ReadAllText(path);
        return [(1, Clean(text))];
    }

    private static List<DocumentChunk> CreateChunks(List<(int Page, string Text)> pages, int maxChars)
    {
        var result = new List<DocumentChunk>();
        var index = 0;

        foreach (var page in pages)
        {
            var text = page.Text;
            for (var i = 0; i < text.Length; i += maxChars)
            {
                var length = Math.Min(maxChars, text.Length - i);
                var chunkText = text.Substring(i, length);

                result.Add(new DocumentChunk
                {
                    Index = index++,
                    Page = page.Page,
                    Text = chunkText,
                    Keywords = ExtractKeywords(chunkText)
                });
            }
        }

        return result;
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        return WordRegex.Matches(text.ToLowerInvariant())
            .Select(x => x.Value)
            .Where(x => x.Length > 3)
            .ToHashSet();
    }

    private static string Clean(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }
}
