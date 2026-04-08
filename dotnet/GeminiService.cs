using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MultiAgentSupportAI.Models;

namespace MultiAgentSupportAI;

/// <summary>
/// Calls the Gemini REST API (generateContent) via HttpClient.
/// Handles the agentic loop: text → function call → tool result → text.
/// Python equivalent: base_agent.py _run_gemini()
/// </summary>
public class GeminiService
{
    private readonly HttpClient     _http;
    private readonly AppSettings    _settings;
    private readonly ILogger<GeminiService> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false,
    };

    public GeminiService(HttpClient http, AppSettings settings, ILogger<GeminiService> logger)
    {
        _http     = http;
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>
    /// Run the full agentic loop for a single user message.
    /// toolDefinitions: list of function declarations (Gemini format).
    /// toolExecutor:    callback that executes a tool and returns its result string.
    /// </summary>
    public async Task<AgentResult> RunAsync(
        string                              systemPrompt,
        string                              userMessage,
        List<GeminiFunctionDeclaration>?    toolDefinitions  = null,
        Func<string, JsonObject, Task<string>>? toolExecutor = null)
    {
        var contents = new List<GeminiContent>
        {
            new("user", [new GeminiPart { Text = userMessage }])
        };

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var response = await CallApiAsync(systemPrompt, contents, toolDefinitions);

                // Agentic loop
                while (true)
                {
                    var candidate = response.Candidates?.FirstOrDefault();
                    if (candidate is null) break;

                    var fnCalls = candidate.Content.Parts
                        .Where(p => p.FunctionCall is not null)
                        .Select(p => p.FunctionCall!)
                        .ToList();

                    if (fnCalls.Count == 0) break;

                    // Add model turn to history
                    contents.Add(candidate.Content);

                    // Execute tools
                    var responseParts = new List<GeminiPart>();
                    foreach (var fc in fnCalls)
                    {
                        _logger.LogInformation("Tool call: {Tool}", fc.Name);
                        var result = toolExecutor is not null
                            ? await toolExecutor(fc.Name, fc.Args ?? new JsonObject())
                            : $"[Tool '{fc.Name}' has no executor]";

                        responseParts.Add(new GeminiPart
                        {
                            FunctionResponse = new GeminiFunctionResponse(
                                fc.Name,
                                new JsonObject { ["result"] = result }
                            )
                        });
                    }

                    contents.Add(new GeminiContent("user", responseParts));
                    response = await CallApiAsync(systemPrompt, contents, toolDefinitions);
                }

                var text = response.Candidates?.FirstOrDefault()?.Content.Parts
                    .Where(p => p.Text is not null)
                    .Select(p => p.Text!)
                    .FirstOrDefault() ?? "";

                return new AgentResult(true, text,
                    new Dictionary<string, object> { ["model"] = _settings.GeminiModel, ["provider"] = "gemini" });
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogWarning("Gemini API error (attempt {Attempt}): {Error}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt));
            }
            catch (Exception ex)
            {
                return new AgentResult(false, "", Error: ex.Message);
            }
        }

        return new AgentResult(false, "", Error: "Max retries exceeded");
    }

    private async Task<GeminiResponse> CallApiAsync(
        string systemPrompt,
        List<GeminiContent> contents,
        List<GeminiFunctionDeclaration>? tools)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_settings.GeminiModel}:generateContent?key={_settings.GeminiApiKey}";

        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } }
            },
            ["contents"]          = JsonSerializer.SerializeToNode(contents, _json),
            ["generationConfig"]  = new JsonObject
            {
                ["temperature"]    = _settings.Temperature,
                ["maxOutputTokens"] = _settings.MaxTokens
            }
        };

        if (tools?.Count > 0)
        {
            body["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["function_declarations"] = JsonSerializer.SerializeToNode(tools, _json)
                }
            };
        }

        var resp = await _http.PostAsJsonAsync(url, body);
        var content = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API error {resp.StatusCode}: {content}");

        return JsonSerializer.Deserialize<GeminiResponse>(content, _json)
            ?? throw new InvalidOperationException("Empty Gemini response");
    }
}

// ── Gemini REST API DTOs ──────────────────────────────────────────────────────

public record GeminiResponse(
    List<GeminiCandidate>? Candidates = null
);

public record GeminiCandidate(
    GeminiContent Content,
    string?       FinishReason = null
);

public record GeminiContent(
    string           Role,
    List<GeminiPart> Parts
);

public class GeminiPart
{
    public string?               Text             { get; set; }
    public GeminiFunctionCall?   FunctionCall     { get; set; }
    public GeminiFunctionResponse? FunctionResponse { get; set; }
}

public record GeminiFunctionCall(
    string      Name,
    JsonObject? Args = null
);

public record GeminiFunctionResponse(
    string      Name,
    JsonObject? Response = null
);

public record GeminiFunctionDeclaration(
    string       Name,
    string       Description,
    JsonObject?  Parameters = null
);
