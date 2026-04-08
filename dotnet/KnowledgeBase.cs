using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MultiAgentSupportAI.Data;

namespace MultiAgentSupportAI;

/// <summary>
/// Loads articles from SQLite and provides keyword-based search.
/// Seeds from knowledge_base.json on first run if the DB is empty.
/// </summary>
public class KnowledgeBase
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<KnowledgeBase>          _logger;

    public KnowledgeBase(IDbContextFactory<AppDbContext> factory, ILogger<KnowledgeBase> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    // ── Document count (used by /health) ─────────────────────────────────────
    public int Count()
    {
        using var db = _factory.CreateDbContext();
        return db.Articles.Count();
    }

    // ── Search ────────────────────────────────────────────────────────────────
    public List<KnowledgeDocument> Search(string query, int topK = 3)
    {
        var tokens = Tokenise(query);
        if (tokens.Count == 0) return [];

        using var db   = _factory.CreateDbContext();
        var       docs = db.Articles.AsNoTracking().ToList();

        return docs
            .Select(d => (doc: ToModel(d), score: Score(tokens, d)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(topK)
            .Select(x => x.doc)
            .ToList();
    }

    // ── Category filter ───────────────────────────────────────────────────────
    public List<KnowledgeDocument> GetByCategory(string category)
    {
        using var db = _factory.CreateDbContext();
        return db.Articles
            .AsNoTracking()
            .Where(a => a.Category.ToLower() == category.ToLower())
            .Select(a => ToModel(a))
            .ToList();
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    public List<KnowledgeDocument> GetAll()
    {
        using var db = _factory.CreateDbContext();
        return db.Articles.AsNoTracking().OrderBy(a => a.Category).ThenBy(a => a.DocId)
            .Select(a => ToModel(a)).ToList();
    }

    public KnowledgeDocument? GetById(int id)
    {
        using var db  = _factory.CreateDbContext();
        var       ent = db.Articles.AsNoTracking().FirstOrDefault(a => a.Id == id);
        return ent is null ? null : ToModel(ent);
    }

    public KnowledgeDocument Add(string docId, string category, string title, string content)
    {
        using var db  = _factory.CreateDbContext();
        var       ent = new ArticleEntity
        {
            DocId     = docId,
            Category  = category.ToLower(),
            Title     = title,
            Content   = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Articles.Add(ent);
        db.SaveChanges();
        _logger.LogInformation("Article added: {DocId}", docId);
        return ToModel(ent);
    }

    public KnowledgeDocument? Update(int id, string? category, string? title, string? content)
    {
        using var db  = _factory.CreateDbContext();
        var       ent = db.Articles.Find(id);
        if (ent is null) return null;

        if (category is not null) ent.Category = category.ToLower();
        if (title    is not null) ent.Title     = title;
        if (content  is not null) ent.Content   = content;
        ent.UpdatedAt = DateTime.UtcNow;

        db.SaveChanges();
        _logger.LogInformation("Article updated: {Id}", id);
        return ToModel(ent);
    }

    public bool Delete(int id)
    {
        using var db  = _factory.CreateDbContext();
        var       ent = db.Articles.Find(id);
        if (ent is null) return false;

        db.Articles.Remove(ent);
        db.SaveChanges();
        _logger.LogInformation("Article deleted: {Id}", id);
        return true;
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    public void SeedFromJson(string jsonPath)
    {
        using var db = _factory.CreateDbContext();
        if (db.Articles.Any()) return;   // already seeded

        if (!File.Exists(jsonPath))
        {
            _logger.LogWarning("Seed file not found: {Path}", jsonPath);
            return;
        }

        var docs = JsonSerializer.Deserialize<List<JsonKbDoc>>(
            File.ReadAllText(jsonPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        foreach (var d in docs)
        {
            db.Articles.Add(new ArticleEntity
            {
                DocId     = d.Id,
                Category  = d.Category.ToLower(),
                Title     = d.Title,
                Content   = d.Content,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        db.SaveChanges();
        _logger.LogInformation("Seeded {Count} articles from JSON", docs.Count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static KnowledgeDocument ToModel(ArticleEntity e) =>
        new(e.Id, e.DocId, e.Category, e.Title, e.Content);

    private static double Score(HashSet<string> queryTokens, ArticleEntity doc)
    {
        var docTokens = Tokenise($"{doc.Title} {doc.Content} {doc.Category}");
        return (double)queryTokens.Intersect(docTokens).Count() / Math.Max(queryTokens.Count, 1);
    }

    private static HashSet<string> Tokenise(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]+")
             .Select(m => m.Value)
             .ToHashSet();

    private record JsonKbDoc(string Id, string Category, string Title, string Content);
}

public record KnowledgeDocument(int Id, string DocId, string Category, string Title, string Content);
