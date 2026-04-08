namespace MultiAgentSupportAI;

/// <summary>
/// Strongly-typed settings — maps to appsettings.json + environment variables.
/// Python equivalent: config.py
/// </summary>
public class AppSettings
{
    public string GeminiApiKey  { get; set; } = "";
    public string GeminiModel   { get; set; } = "gemini-2.5-flash";
    public string LlmProvider   { get; set; } = "gemini";   // gemini | anthropic
    public float  Temperature   { get; set; } = 0.7f;
    public int    MaxTokens     { get; set; } = 2048;
    public bool   DemoMode      { get; set; } = false;
    public string AnthropicApiKey { get; set; } = "";
    public string AnthropicModel  { get; set; } = "claude-3-5-sonnet-20241022";
}
