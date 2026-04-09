namespace MultiAgentSupportAI.Models;

// ── Request models ────────────────────────────────────────────────────────────

public record CustomerMessage(
    string  Message,
    string? SessionId = null
);

public record FeedbackRequest(
    string  SessionId,
    int     Rating,       // 1-5
    string? Comment = null
);

// ── Response models ───────────────────────────────────────────────────────────

public record ClassificationResponse(
    string        SessionId,
    string        Intent,
    double        Confidence,
    string        Reasoning,
    List<string>  Keywords,
    string        Urgency
);

public record SupportResponse(
    string SessionId,
    string Intent,
    double Confidence,
    string Answer,
    bool   SourcesSearched
);

// ── Knowledge base article management ────────────────────────────────────────

public record ArticleRequest(
    string DocId,
    string Category,
    string Title,
    string Content
);

public record ArticleUpdateRequest(
    string? Category = null,
    string? Title    = null,
    string? Content  = null
);

public record HealthResponse(
    string Status,
    string Model,
    string Provider,
    int    KnowledgeBaseDocuments,
    double UptimeSeconds
);

// ── Agent internals ───────────────────────────────────────────────────────────

public record AgentResult(
    bool                    Success,
    string                  Response,
    Dictionary<string,object>? Metadata = null,
    string?                 Error    = null
);

public record ClassificationResult(
    string       Intent,
    double       Confidence,
    string       Reasoning,
    List<string> Keywords,
    string       Urgency
);
