using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyBrainAI.Api.Services;

public sealed class KnowledgeStore
{
    private static readonly Regex WordRegex = new("[a-zA-ZÀ-ÿ0-9]{3,}", RegexOptions.Compiled);
    private readonly object sync = new();
    private readonly string dataFile;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private List<KnowledgeEntry> entries = [];

    public KnowledgeStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDirectory);
        dataFile = Path.Combine(dataDirectory, "knowledge-store.json");
        Load();
    }

    public IReadOnlyList<KnowledgeEntry> GetAll()
    {
        lock (sync)
        {
            return entries
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
    }

    public KnowledgeEntry AddMemory(string title, string content, IEnumerable<string> tags)
    {
        var entry = new KnowledgeEntry
        {
            Type = "memory",
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled memory" : title.Trim(),
            Content = content.Trim(),
            Tags = tags.Select(Normalize).Where(x => x.Length > 0).Distinct().ToList()
        };

        Add(entry);
        return entry;
    }

    public KnowledgeEntry AddCorrection(string question, string correctAnswer, Guid? documentId)
    {
        var entry = new KnowledgeEntry
        {
            Type = "correction",
            Title = $"Correction: {question.Trim()}",
            Content = correctAnswer.Trim(),
            Tags = ["feedback", "correction", .. ExtractTerms(question)],
            SourceDocumentId = documentId
        };

        Add(entry);
        return entry;
    }

    public void AddDocument(ProcessedDocument document)
    {
        var documentEntries = document.Chunks.Select(chunk => new KnowledgeEntry
        {
            Type = "document",
            Title = $"{document.FileName} - chunk {chunk.Index + 1}",
            Content = chunk.Text,
            Tags = chunk.Keywords.Select(NormalizeTerm).Take(80).ToList(),
            SourceDocumentId = document.Id,
            SourceFileName = document.FileName
        });

        lock (sync)
        {
            entries.RemoveAll(x => x.SourceDocumentId == document.Id && x.Type == "document");
            entries.AddRange(documentEntries);
            Save();
        }
    }

    public bool ContainsDocument(Guid documentId)
    {
        lock (sync)
        {
            return entries.Any(x => x.Type == "document" && x.SourceDocumentId == documentId);
        }
    }

    public KnowledgeAnswer Ask(string question, Guid? documentId = null)
    {
        var queryTerms = ExpandTerms(ExtractTerms(question));
        var candidates = Search(question, queryTerms, documentId, 6);

        if (candidates.Count == 0)
        {
            return new KnowledgeAnswer
            {
                Answer = "Ainda nao aprendi informacao suficiente para responder isso. Voce pode me ensinar a resposta correta no painel de aprendizado.",
                Sources = []
            };
        }

        var correction = candidates.FirstOrDefault(x => x.Entry.Type == "correction");
        if (correction is not null && correction.Score >= 6)
        {
            return new KnowledgeAnswer
            {
                Answer = correction.Entry.Content,
                Sources = [ToSource(correction.Entry)]
            };
        }

        var answerLines = candidates
            .Take(4)
            .Select(x => $"- {BestSnippet(x.Entry.Content, queryTerms)}")
            .ToList();

        return new KnowledgeAnswer
        {
            Answer = "Resposta local baseada no conhecimento aprendido:\n\n" + string.Join("\n", answerLines),
            Sources = candidates.Take(4).Select(x => ToSource(x.Entry)).ToList()
        };
    }

    private List<ScoredEntry> Search(string question, HashSet<string> queryTerms, Guid? documentId, int limit)
    {
        lock (sync)
        {
            return entries
                .Where(x => documentId is null || x.SourceDocumentId == documentId || x.Type != "document")
                .Select(entry => new ScoredEntry(entry, Score(entry, queryTerms, question)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Entry.CreatedAt)
                .Take(limit)
                .ToList();
        }
    }

    private static int Score(KnowledgeEntry entry, HashSet<string> queryTerms, string question)
    {
        var entryTerms = ExtractTerms($"{entry.Title} {entry.Content} {string.Join(' ', entry.Tags)}");
        var score = entryTerms.Intersect(queryTerms).Count() * 3;

        if (entry.Type == "correction")
            score += 4;

        if (LooksLikeCvQuestion(question) && ContainsAny(entryTerms, "experiencia", "profissional", "empresa", "cliente", "cargo", "carreira", "projeto"))
            score += 4;

        if (LooksLikeCountQuestion(question) && Regex.IsMatch(entry.Content, @"\b(empresa|cliente|companhia|consultoria)\b", RegexOptions.IgnoreCase))
            score += 3;

        return score;
    }

    private static bool LooksLikeCvQuestion(string question)
    {
        var normalized = Normalize(question);
        return normalized.Contains("cv", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("curriculo", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("empresa", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("experiencia", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCountQuestion(string question)
    {
        var normalized = Normalize(question);
        return normalized.Contains("quant", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("numero", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("total", StringComparison.OrdinalIgnoreCase);
    }

    private static KnowledgeSource ToSource(KnowledgeEntry entry)
    {
        return new KnowledgeSource
        {
            Id = entry.Id,
            Type = entry.Type,
            Title = entry.Title,
            Preview = entry.Content.Length > 260 ? entry.Content[..260] + "..." : entry.Content,
            SourceFileName = entry.SourceFileName
        };
    }

    private static string BestSnippet(string content, HashSet<string> queryTerms)
    {
        var parts = Regex.Split(content, @"(?<=[.!?])\s+")
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        if (parts.Count == 0)
            return content;

        return parts
            .OrderByDescending(part => ExtractTerms(part).Intersect(queryTerms).Count())
            .First();
    }

    private void Add(KnowledgeEntry entry)
    {
        lock (sync)
        {
            entries.Add(entry);
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(dataFile))
            return;

        var json = File.ReadAllText(dataFile);
        var loadedEntries = JsonSerializer.Deserialize<List<KnowledgeEntry>>(json, jsonOptions) ?? [];
        entries = loadedEntries.Select(MigrateBrand).ToList();

        var migrated = false;
        for (var i = 0; i < loadedEntries.Count; i++)
        {
            if (loadedEntries[i].Content != entries[i].Content || loadedEntries[i].Title != entries[i].Title)
            {
                migrated = true;
                break;
            }
        }

        if (migrated)
            Save();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(entries, jsonOptions);
        File.WriteAllText(dataFile, json);
    }

    private static HashSet<string> ExtractTerms(string text)
    {
        return WordRegex.Matches(Normalize(text))
            .Select(x => NormalizeTerm(x.Value))
            .Where(x => x.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExpandTerms(HashSet<string> terms)
    {
        var expanded = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms.ToList())
        {
            foreach (var alias in Aliases(term))
            {
                expanded.Add(alias);
            }
        }

        return expanded;
    }

    private static IEnumerable<string> Aliases(string term)
    {
        return term switch
        {
            "empresa" => ["companhia", "cliente", "consultoria", "experiencia", "profissional", "cargo"],
            "experiencia" => ["atuacao", "profissional", "empresa", "cargo", "carreira", "trabalho"],
            "tempo" => ["periodo", "anos", "meses", "desde", "ate", "inicio", "fim"],
            "cv" => ["curriculo", "experiencia", "empresa", "cargo", "profissional"],
            "curriculo" => ["cv", "experiencia", "empresa", "cargo", "profissional"],
            _ => []
        };
    }

    private static bool ContainsAny(HashSet<string> values, params string[] candidates)
    {
        return candidates.Any(values.Contains);
    }

    private static string NormalizeTerm(string value)
    {
        var normalized = Normalize(value);

        if (normalized.EndsWith("oes", StringComparison.OrdinalIgnoreCase) && normalized.Length > 5)
            return normalized[..^3] + "ao";

        if (normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase) && normalized.Length > 4)
            return normalized[..^1];

        return normalized;
    }

    private static KnowledgeEntry MigrateBrand(KnowledgeEntry entry)
    {
        return new KnowledgeEntry
        {
            Id = entry.Id,
            Type = entry.Type,
            Title = ReplaceOldBrand(entry.Title),
            Content = ReplaceOldBrand(entry.Content),
            Tags = entry.Tags.Select(ReplaceOldBrand).ToList(),
            SourceDocumentId = entry.SourceDocumentId,
            SourceFileName = entry.SourceFileName is null ? null : ReplaceOldBrand(entry.SourceFileName),
            CreatedAt = entry.CreatedAt
        };
    }

    private static string ReplaceOldBrand(string value)
    {
        return value
            .Replace("NeuroDocs AI", "My Brain AI", StringComparison.OrdinalIgnoreCase)
            .Replace("NeuroDocs-AI", "My-Brain-AI", StringComparison.OrdinalIgnoreCase)
            .Replace("AI Document Assistant", "My Brain AI", StringComparison.OrdinalIgnoreCase);
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

    private sealed record ScoredEntry(KnowledgeEntry Entry, int Score);
}
