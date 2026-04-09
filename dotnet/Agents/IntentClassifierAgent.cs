using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MultiAgentSupportAI.Models;

namespace MultiAgentSupportAI.Agents;

/// <summary>
/// Classifies customer queries into: billing | technical | account | general | complaint | unknown
/// Python equivalent: intent_classifier.py IntentClassifierAgent
/// </summary>
public class IntentClassifierAgent : BaseAgent
{
    private static readonly HashSet<string> ValidIntents =
        ["billing", "technical", "account", "general", "complaint", "unknown"];

    private readonly Queue<(string Query, string Intent)> _history = new();
    private const int HistoryWindow = 20;

    public IntentClassifierAgent(ILlmService llm, AppSettings settings, ILogger<IntentClassifierAgent> logger)
        : base(llm, settings, logger) { }

    // ── BaseAgent interface ───────────────────────────────────────────────────

    protected override string GetSystemPrompt() => """
        You are a customer-support intent classifier.
        You MUST respond with ONLY a raw JSON object. No markdown, no code fences, no explanation, no extra text.
        Start your response with { and end with }.

        Required JSON fields:
          "intent"     – exactly one of: billing, technical, account, general, complaint, unknown
          "confidence" – a number between 0.0 and 1.0
          "reasoning"  – one sentence explaining your choice
          "keywords"   – array of 1-5 keyword strings
          "urgency"    – exactly one of: low, medium, high

        Intent guidelines:
          billing   → payments, invoices, charges, refunds, subscriptions, pricing
          technical → bugs, errors, crashes, performance, API, integration
          account   → login, password, profile, permissions, 2FA, email change
          general   → onboarding, how-to, feature questions, general inquiries
          complaint → frustration, service failure, escalation requests
          unknown   → cannot determine from available text

        Example response:
        {"intent":"billing","confidence":0.95,"reasoning":"User asked about a charge on their account.","keywords":["charge","billing","invoice"],"urgency":"medium"}
        """;

    // No tools — keeps the classifier simple and compatible with all LLM providers.
    // Small models (Groq/Ollama) return inconsistent output when tool-calling is involved.
    protected override List<GeminiFunctionDeclaration> GetAvailableTools() => [];

    protected override Task<string> ExecuteToolAsync(string toolName, JsonObject toolInput)
    {
        if (toolName == "check_historical_patterns")
        {
            var recent = _history.TakeLast(10).Select(h => new { query = h.Query, intent = h.Intent });
            return Task.FromResult(JsonSerializer.Serialize(new { patterns = recent }));
        }
        return base.ExecuteToolAsync(toolName, toolInput);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<ClassificationResult> ClassifyAsync(string query)
    {
        Logger.LogInformation("classify_start: {Query}", query[..Math.Min(100, query.Length)]);

        if (Settings.DemoMode)
            return DemoClassify(query);

        var result = await RunAsync(query);

        if (!result.Success)
        {
            Logger.LogError("classify_failed: {Error}", result.Error);
            return Fallback($"Classification failed: {result.Error}");
        }

        Logger.LogInformation("classify_raw_response: {Raw}", result.Response);
        var parsed = ParseResponse(result.Response);
        _history.Enqueue((query[..Math.Min(100, query.Length)], parsed.Intent));
        if (_history.Count > HistoryWindow) _history.Dequeue();

        Logger.LogInformation("intent={Intent} confidence={Confidence}", parsed.Intent, parsed.Confidence);
        return parsed;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private ClassificationResult ParseResponse(string raw)
    {
        var text = raw.Trim();

        // Strip markdown fences (```json ... ``` or ``` ... ```)
        if (text.StartsWith("```"))
        {
            var lines = text.Split('\n');
            text = string.Join('\n', lines[1..^1]).Trim();
        }

        // Extract first JSON object if LLM added surrounding text
        var jsonMatch = System.Text.RegularExpressions.Regex.Match(text, @"\{[\s\S]*\}");
        if (jsonMatch.Success) text = jsonMatch.Value;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var intent = root.TryGetProperty("intent", out var i)
                ? i.GetString()?.ToLower() ?? "unknown"
                : "unknown";

            if (!ValidIntents.Contains(intent)) intent = "unknown";

            return new ClassificationResult(
                Intent:     intent,
                Confidence: root.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5,
                Reasoning:  root.TryGetProperty("reasoning",  out var r) ? r.GetString() ?? "" : "",
                Keywords:   root.TryGetProperty("keywords",   out var k)
                                ? k.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                                : [],
                Urgency:    root.TryGetProperty("urgency",    out var u) ? u.GetString() ?? "medium" : "medium"
            );
        }
        catch
        {
            return Fallback("JSON parse error");
        }
    }

    private static ClassificationResult DemoClassify(string query)
    {
        var q = query.ToLower();
        var rules = new List<(string Intent, string[] Keywords)>
        {
            ("billing",   ["invoice","charge","payment","refund","subscription","billing","price","cost","fee","paid","money","charged","twice"]),
            ("technical", ["crash","bug","error","broken","not working","slow","fail","issue","problem","fix","api","install","crashing"]),
            ("account",   ["login","password","sign in","account","profile","email","username","forgot","reset","locked","access"]),
            ("complaint", ["angry","terrible","unacceptable","worst","awful","manager","lawsuit","furious"]),
            ("general",   ["how","what","where","guide","started","help","learn","tutorial","feature","info"]),
        };

        foreach (var (intent, keywords) in rules)
        {
            var hits = keywords.Where(k => q.Contains(k)).ToList();
            if (hits.Count > 0)
            {
                return new ClassificationResult(
                    Intent:     intent,
                    Confidence: Math.Min(0.7 + hits.Count * 0.05, 0.98),
                    Reasoning:  $"Keywords matched: {string.Join(", ", hits)}",
                    Keywords:   hits[..Math.Min(5, hits.Count)],
                    Urgency:    intent is "complaint" or "technical" ? "high" : intent == "account" ? "medium" : "low"
                );
            }
        }

        return Fallback("no keywords matched");
    }

    private static ClassificationResult Fallback(string reason) => new(
        Intent:     "unknown",
        Confidence: 0.0,
        Reasoning:  $"Classification failed: {reason}",
        Keywords:   [],
        Urgency:    "medium"
    );
}
