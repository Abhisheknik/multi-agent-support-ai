using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MultiAgentSupportAI.Models;

namespace MultiAgentSupportAI;

/// <summary>
/// OpenAI-compatible LLM client — works with Groq (cloud, free tier)
/// and Ollama (local). Both expose the standard /v1/chat/completions endpoint.
///
/// Provider selection via AppSettings.LlmProvider:
///   "groq"   → api.groq.com  (free 30 req/min, llama-3.1-8b-instant)
///   "ollama" → localhost:11434 (fully offline, phi3:mini)
/// </summary>
public class OpenAiCompatibleService : ILlmService
{
    private readonly HttpClient  _http;
    private readonly AppSettings _settings;
    private readonly ILogger<OpenAiCompatibleService> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAiCompatibleService(HttpClient http, AppSettings settings, ILogger<OpenAiCompatibleService> logger)
    {
        _http     = http;
        _settings = settings;
        _logger   = logger;
    }

    public async Task<AgentResult> RunAsync(
        string                                  systemPrompt,
        string                                  userMessage,
        List<GeminiFunctionDeclaration>?        toolDefinitions = null,
        Func<string, JsonObject, Task<string>>? toolExecutor    = null)
    {
        var (baseUrl, model, apiKey) = GetProviderConfig();

        var messages = new List<JsonObject>
        {
            new() { ["role"] = "system", ["content"] = systemPrompt },
            new() { ["role"] = "user",   ["content"] = userMessage  }
        };

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var response = await CallApiAsync(baseUrl, model, apiKey, messages, toolDefinitions);

                // Agentic tool-call loop
                while (true)
                {
                    var choice  = response?["choices"]?[0];
                    var message = choice?["message"];
                    if (message is null) break;

                    var toolCalls = message["tool_calls"]?.AsArray();
                    if (toolCalls is null || toolCalls.Count == 0) break;

                    // Add assistant turn to history
                    messages.Add(JsonNode.Parse(message.ToJsonString())!.AsObject());

                    // Execute each tool call
                    foreach (var tc in toolCalls)
                    {
                        var toolId   = tc?["id"]?.GetValue<string>() ?? "";
                        var fnName   = tc?["function"]?["name"]?.GetValue<string>() ?? "";
                        var fnArgs   = tc?["function"]?["arguments"]?.GetValue<string>() ?? "{}";

                        _logger.LogInformation("Tool call: {Tool}", fnName);

                        JsonObject argsObj;
                        try { argsObj = JsonNode.Parse(fnArgs)?.AsObject() ?? new JsonObject(); }
                        catch { argsObj = new JsonObject(); }

                        var result = toolExecutor is not null
                            ? await toolExecutor(fnName, argsObj)
                            : $"[Tool '{fnName}' has no executor]";

                        messages.Add(new JsonObject
                        {
                            ["role"]         = "tool",
                            ["tool_call_id"] = toolId,
                            ["content"]      = result
                        });
                    }

                    response = await CallApiAsync(baseUrl, model, apiKey, messages, toolDefinitions);
                }

                var text = response?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
                return new AgentResult(true, text,
                    new Dictionary<string, object> { ["model"] = model, ["provider"] = _settings.LlmProvider });
            }
            catch (RateLimitException rle) when (attempt < 4)
            {
                _logger.LogWarning("Rate limit hit — waiting {S}s before retry {A}", rle.RetryAfterSeconds, attempt);
                await Task.Delay(TimeSpan.FromSeconds(rle.RetryAfterSeconds + 1));
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogWarning("{Provider} error (attempt {A}): {E}", _settings.LlmProvider, attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
            catch (Exception ex)
            {
                return new AgentResult(false, "", Error: ex.Message);
            }
        }

        return new AgentResult(false, "", Error: "Max retries exceeded");
    }

    private async Task<JsonNode?> CallApiAsync(
        string              baseUrl,
        string              model,
        string              apiKey,
        List<JsonObject>    messages,
        List<GeminiFunctionDeclaration>? tools)
    {
        var url = $"{baseUrl.TrimEnd('/')}/v1/chat/completions";

        var body = new JsonObject
        {
            ["model"]       = model,
            ["messages"]    = JsonNode.Parse(JsonSerializer.Serialize(messages, _json)),
            ["temperature"] = _settings.Temperature,
            ["max_tokens"]  = _settings.MaxTokens,
        };

        if (tools?.Count > 0)
        {
            body["tools"] = JsonNode.Parse(JsonSerializer.Serialize(
                tools.Select(t => new
                {
                    type     = "function",
                    function = new
                    {
                        name        = t.Name,
                        description = t.Description,
                        parameters  = t.Parameters
                    }
                }), _json));
            body["tool_choice"] = "auto";
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: _json)
        };

        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var resp    = await _http.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // Parse "Please try again in 13.39s" from Groq's error body
                var match = System.Text.RegularExpressions.Regex.Match(content, @"try again in (\d+(?:\.\d+)?)s");
                var seconds = match.Success ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 15;
                throw new RateLimitException($"{_settings.LlmProvider} rate limit", seconds);
            }
            throw new HttpRequestException($"{_settings.LlmProvider} API error {resp.StatusCode}: {content}");
        }

        return JsonNode.Parse(content);
    }

    private (string baseUrl, string model, string apiKey) GetProviderConfig() =>
        _settings.LlmProvider.ToLower() switch
        {
            "groq"   => ("https://api.groq.com/openai", _settings.GroqModel,   _settings.GroqApiKey),
            "ollama" => (_settings.OllamaBaseUrl,        _settings.OllamaModel, ""),
            _        => throw new InvalidOperationException($"Unknown provider: {_settings.LlmProvider}")
        };
}

public class RateLimitException(string message, double retryAfterSeconds) : Exception(message)
{
    public double RetryAfterSeconds { get; } = retryAfterSeconds;
}
