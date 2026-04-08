using System.Text.Json;
using System.Text.Json.Nodes;
using MultiAgentSupportAI.Models;

namespace MultiAgentSupportAI.Agents;

/// <summary>
/// Searches the knowledge base and drafts grounded answers.
/// Python equivalent: knowledge_retriever.py KnowledgeRetrieverAgent
/// </summary>
public class KnowledgeRetrieverAgent : BaseAgent
{
    public KnowledgeBase KnowledgeBase { get; }

    public KnowledgeRetrieverAgent(
        GeminiService                        gemini,
        AppSettings                          settings,
        KnowledgeBase                        kb,
        ILogger<KnowledgeRetrieverAgent>     logger)
        : base(gemini, settings, logger)
    {
        KnowledgeBase = kb;
    }

    // ── BaseAgent interface ───────────────────────────────────────────────────

    protected override string GetSystemPrompt() => """
        You are a customer-support specialist.
        Answer the customer's question using ONLY the information returned by your search tools.
        If the knowledge base does not contain a relevant answer, say so politely and suggest
        contacting human support. Be concise, friendly, and accurate. Do not invent information.
        """;

    protected override List<GeminiFunctionDeclaration> GetAvailableTools() =>
    [
        new GeminiFunctionDeclaration(
            Name:        "search_knowledge_base",
            Description: "Search the knowledge base with a free-text query.",
            Parameters:  JsonNode.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string", "description": "Search query" },
                    "top_k": { "type": "string", "description": "Max results (default 3)" }
                  },
                  "required": ["query"]
                }
                """)!.AsObject()
        ),
        new GeminiFunctionDeclaration(
            Name:        "get_category_docs",
            Description: "Return all articles in a given category.",
            Parameters:  JsonNode.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "category": { "type": "string", "description": "billing | technical | account | general" }
                  },
                  "required": ["category"]
                }
                """)!.AsObject()
        )
    ];

    protected override Task<string> ExecuteToolAsync(string toolName, JsonObject toolInput)
    {
        if (toolName == "search_knowledge_base")
        {
            var query = toolInput["query"]?.GetValue<string>() ?? "";
            var topK  = int.TryParse(toolInput["top_k"]?.GetValue<string>(), out var k) ? k : 3;
            var docs  = KnowledgeBase.Search(query, topK);
            Logger.LogInformation("kb_search: query={Query} hits={Hits}", query, docs.Count);
            return Task.FromResult(docs.Count == 0
                ? JsonSerializer.Serialize(new { results = Array.Empty<object>(), message = "No matching documents found." })
                : JsonSerializer.Serialize(new { results = docs }));
        }

        if (toolName == "get_category_docs")
        {
            var category = toolInput["category"]?.GetValue<string>() ?? "";
            var docs     = KnowledgeBase.GetByCategory(category);
            Logger.LogInformation("kb_category: category={Category} hits={Hits}", category, docs.Count);
            return Task.FromResult(docs.Count == 0
                ? JsonSerializer.Serialize(new { results = Array.Empty<object>(), message = $"No documents in '{category}'." })
                : JsonSerializer.Serialize(new { results = docs }));
        }

        return base.ExecuteToolAsync(toolName, toolInput);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<AgentResult> RetrieveAndAnswerAsync(string query)
    {
        Logger.LogInformation("retrieve_start: {Query}", query[..Math.Min(100, query.Length)]);

        if (Settings.DemoMode)
            return DemoAnswer(query);

        var result = await RunAsync(query);
        if (result.Success)
            Logger.LogInformation("retrieve_complete: length={Length}", result.Response.Length);
        else
            Logger.LogError("retrieve_failed: {Error}", result.Error);

        return result;
    }

    private AgentResult DemoAnswer(string query)
    {
        var docs = KnowledgeBase.Search(query, 2);
        var answer = docs.Count == 0
            ? "I couldn't find a specific article for your question. Please contact support@example.com or use live chat (9am–6pm EST)."
            : string.Join("\n\n", docs.Select(d => $"**{d.Title}**\n{d.Content}"));

        return new AgentResult(true, answer,
            new Dictionary<string, object> { ["demo_mode"] = true, ["docs_used"] = docs.Count });
    }
}
