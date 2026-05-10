using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;

namespace AiDocumentAssistant.Api.Services;

public sealed class RagService
{
    private static readonly Regex WordRegex = new("[a-zA-ZÀ-ÿ0-9]{3,}", RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex DateLikeRegex = new(@"\b(0?[1-9]|1[0-2])[/.-](19|20)?\d{2}\b|\b(19|20)\d{2}\b", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "o", "os", "as", "um", "uma", "uns", "umas", "de", "da", "do", "das", "dos",
        "em", "no", "na", "nos", "nas", "por", "para", "com", "sem", "que", "qual", "quais",
        "quanto", "quantos", "quanta", "quantas", "tempo", "teve", "tem", "foi", "era", "ele",
        "ela", "meu", "minha", "meus", "minhas", "seu", "sua", "seus", "suas", "the", "and",
        "for", "with", "from", "what", "when", "where", "how", "his", "her", "this", "that"
    };

    private static readonly Dictionary<string, string[]> QueryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["atuacao"] = ["experiencia", "profissional", "trabalho", "carreira", "cargo", "empresa", "freelancer", "consultor", "desenvolvedor"],
        ["real"] = ["experiencia", "profissional", "trabalho", "empresa", "projeto", "projetos"],
        ["experiencia"] = ["atuacao", "profissional", "trabalho", "carreira", "cargo", "empresa"],
        ["anos"] = ["periodo", "desde", "ate", "inicio", "fim", "data", "datas"],
        ["tempo"] = ["periodo", "anos", "meses", "desde", "ate", "inicio", "fim", "data", "datas"],
        ["cv"] = ["curriculo", "experiencia", "profissional", "formacao", "skills", "habilidades"]
    };

    public Task<RagAnswer> AnswerAsync(ProcessedDocument document, string question)
    {
        var queryTerms = ExtractTerms(question);
        var expandedTerms = ExpandTerms(queryTerms);
        var asksAboutDuration = IsDurationQuestion(question, expandedTerms);

        var topChunks = document.Chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = ScoreChunk(chunk, expandedTerms, asksAboutDuration)
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
            "Com base nos trechos mais relevantes do documento, encontrei isto:\n\n" +
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
        var questionTerms = ExpandTerms(ExtractTerms(question));
        var asksAboutDuration = IsDurationQuestion(question, questionTerms);

        var sentences = chunks
            .SelectMany(c => c.Text.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => new { c.Page, Text = s.Trim() }))
            .Where(x => x.Text.Length > 25)
            .Select(x => new
            {
                x.Page,
                x.Text,
                Score = ScoreText(x.Text, questionTerms, asksAboutDuration)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(6)
            .Select(x => $"- {x.Text}. [page {x.Page}]");

        var result = string.Join("\n", sentences);
        return string.IsNullOrWhiteSpace(result)
            ? string.Join("\n", chunks.Take(3).Select(x => $"- {x.Text} [page {x.Page}]"))
            : result;
    }

    private static HashSet<string> ExtractTerms(string text)
    {
        return WordRegex.Matches(Normalize(text))
            .Select(x => x.Value)
            .Where(x => x.Length > 2)
            .Where(x => !StopWords.Contains(x))
            .ToHashSet();
    }

    private static HashSet<string> ExpandTerms(HashSet<string> terms)
    {
        var expanded = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            if (!QueryAliases.TryGetValue(term, out var aliases))
                continue;

            foreach (var alias in aliases)
            {
                expanded.Add(alias);
            }
        }

        return expanded;
    }

    private static int ScoreChunk(DocumentChunk chunk, HashSet<string> queryTerms, bool asksAboutDuration)
    {
        var chunkTerms = chunk.Keywords.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var score = chunkTerms.Intersect(queryTerms).Count() * 3;

        if (asksAboutDuration && DateLikeRegex.IsMatch(chunk.Text))
            score += 4;

        if (asksAboutDuration && ContainsAny(chunkTerms, "experiencia", "profissional", "trabalho", "empresa", "cargo", "carreira"))
            score += 3;

        return score;
    }

    private static int ScoreText(string text, HashSet<string> queryTerms, bool asksAboutDuration)
    {
        var textTerms = ExtractTerms(text);
        var score = textTerms.Intersect(queryTerms).Count() * 3;

        if (asksAboutDuration && DateLikeRegex.IsMatch(text))
            score += 4;

        if (asksAboutDuration && ContainsAny(textTerms, "experiencia", "profissional", "trabalho", "empresa", "cargo", "carreira"))
            score += 3;

        return score;
    }

    private static bool IsDurationQuestion(string question, HashSet<string> terms)
    {
        var normalized = Normalize(question);
        return terms.Contains("tempo")
            || terms.Contains("anos")
            || terms.Contains("periodo")
            || normalized.Contains("quanto tempo", StringComparison.OrdinalIgnoreCase)
            || YearRegex.IsMatch(normalized);
    }

    private static bool ContainsAny(HashSet<string> values, params string[] candidates)
    {
        return candidates.Any(values.Contains);
    }

    private static string Normalize(string value)
    {
        var normalized = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
