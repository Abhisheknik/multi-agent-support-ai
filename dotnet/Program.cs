using Microsoft.EntityFrameworkCore;
using MultiAgentSupportAI;
using MultiAgentSupportAI.Agents;
using MultiAgentSupportAI.Data;
using MultiAgentSupportAI.Models;
using Scalar.AspNetCore;
using Serilog;

// ── Serilog ───────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

// ── Settings ──────────────────────────────────────────────────────────────────
var cfg = new AppSettings();
builder.Configuration.Bind("App", cfg);
cfg.GeminiApiKey = builder.Configuration["GEMINI_API_KEY"] ?? cfg.GeminiApiKey;
cfg.GeminiModel  = builder.Configuration["GEMINI_MODEL"]   ?? cfg.GeminiModel;
cfg.LlmProvider  = builder.Configuration["LLM_PROVIDER"]   ?? cfg.LlmProvider;
cfg.DemoMode     = bool.TryParse(builder.Configuration["DEMO_MODE"], out var dm) ? dm : cfg.DemoMode;

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddSingleton(cfg);

// SQLite — use factory so singletons can safely create scoped DbContexts
builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlite("Data Source=support_ai.db"));

builder.Services.AddHttpClient<GeminiService>();
builder.Services.AddSingleton<KnowledgeBase>();
builder.Services.AddSingleton<IntentClassifierAgent>();
builder.Services.AddSingleton<KnowledgeRetrieverAgent>();

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseDefaultFiles();   // serves index.html at /
app.UseStaticFiles();    // serves wwwroot/
app.MapOpenApi();
app.MapScalarApiReference(o => o.Title = "Multi-Agent Support AI");

// ── DB init + seed ────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();   // creates tables if they don't exist
}

// Seed from JSON if DB is empty
var kb      = app.Services.GetRequiredService<KnowledgeBase>();
var seedPath = Path.Combine(AppContext.BaseDirectory, "data", "knowledge_base.json");
kb.SeedFromJson(seedPath);

var startTime = DateTime.UtcNow;

// ── GET /health ───────────────────────────────────────────────────────────────
app.MapGet("/health", (KnowledgeBase kb) =>
    Results.Ok(new HealthResponse(
        Status:                 "healthy",
        Model:                  cfg.GeminiModel,
        KnowledgeBaseDocuments: kb.Count(),
        UptimeSeconds:          (DateTime.UtcNow - startTime).TotalSeconds
    )));

// ── POST /classify ────────────────────────────────────────────────────────────
app.MapPost("/classify", async (CustomerMessage body, IntentClassifierAgent classifier) =>
{
    if (string.IsNullOrWhiteSpace(body.Message))
        return Results.BadRequest(new { detail = "message is required" });

    var result = await classifier.ClassifyAsync(body.Message);
    return Results.Ok(new ClassificationResponse(
        SessionId:  body.SessionId ?? Guid.NewGuid().ToString(),
        Intent:     result.Intent,
        Confidence: result.Confidence,
        Reasoning:  result.Reasoning,
        Keywords:   result.Keywords,
        Urgency:    result.Urgency
    ));
});

// ── POST /support ─────────────────────────────────────────────────────────────
app.MapPost("/support", async (
    CustomerMessage         body,
    IntentClassifierAgent   classifier,
    KnowledgeRetrieverAgent retriever) =>
{
    if (string.IsNullOrWhiteSpace(body.Message))
        return Results.BadRequest(new { detail = "message is required" });

    var classification = await classifier.ClassifyAsync(body.Message);
    var retrieval      = await retriever.RetrieveAndAnswerAsync(body.Message);

    if (!retrieval.Success)
        return Results.Problem(retrieval.Error ?? "Retrieval failed", statusCode: 502);

    return Results.Ok(new SupportResponse(
        SessionId:       body.SessionId ?? Guid.NewGuid().ToString(),
        Intent:          classification.Intent,
        Confidence:      classification.Confidence,
        Answer:          retrieval.Response,
        SourcesSearched: true
    ));
});

// ── POST /feedback ────────────────────────────────────────────────────────────
app.MapPost("/feedback", (FeedbackRequest body, ILogger<Program> logger) =>
{
    logger.LogInformation("feedback: session={S} rating={R}", body.SessionId, body.Rating);
    return Results.Ok(new { status = "received", session_id = body.SessionId });
});

// ═════════════════════════════════════════════════════════════════════════════
// KNOWLEDGE BASE CRUD — add/edit/delete articles without restarting the server
// ═════════════════════════════════════════════════════════════════════════════

// GET /articles — list all
app.MapGet("/articles", (KnowledgeBase kb) =>
    Results.Ok(kb.GetAll()))
    .WithTags("Knowledge Base");

// GET /articles/{id} — get one
app.MapGet("/articles/{id:int}", (int id, KnowledgeBase kb) =>
{
    var article = kb.GetById(id);
    return article is null ? Results.NotFound() : Results.Ok(article);
}).WithTags("Knowledge Base");

// POST /articles — add new article (agent uses it immediately)
app.MapPost("/articles", (ArticleRequest body, KnowledgeBase kb) =>
{
    if (string.IsNullOrWhiteSpace(body.Title) || string.IsNullOrWhiteSpace(body.Content))
        return Results.BadRequest(new { detail = "title and content are required" });

    var article = kb.Add(body.DocId, body.Category, body.Title, body.Content);
    return Results.Created($"/articles/{article.Id}", article);
}).WithTags("Knowledge Base");

// PUT /articles/{id} — update existing article
app.MapPut("/articles/{id:int}", (int id, ArticleUpdateRequest body, KnowledgeBase kb) =>
{
    var updated = kb.Update(id, body.Category, body.Title, body.Content);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).WithTags("Knowledge Base");

// DELETE /articles/{id} — remove article
app.MapDelete("/articles/{id:int}", (int id, KnowledgeBase kb) =>
{
    var deleted = kb.Delete(id);
    return deleted ? Results.Ok(new { message = $"Article {id} deleted" }) : Results.NotFound();
}).WithTags("Knowledge Base");

app.Run();
