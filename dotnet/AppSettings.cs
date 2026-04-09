namespace MultiAgentSupportAI;

/// <summary>
/// Strongly-typed settings — maps to appsettings.json + environment variables.
/// Python equivalent: config.py
/// </summary>
public class AppSettings
{
    // ── Provider switch ───────────────────────────────────────────────────────
    /// <summary>gemini | groq | ollama</summary>
    public string LlmProvider   { get; set; } = "gemini";

    // ── Gemini ────────────────────────────────────────────────────────────────
    public string GeminiApiKey  { get; set; } = "";
    public string GeminiModel   { get; set; } = "gemini-2.5-flash";

    // ── Groq (free tier, OpenAI-compatible) ───────────────────────────────────
    public string GroqApiKey    { get; set; } = "";
    public string GroqModel     { get; set; } = "llama-3.1-8b-instant";

    // ── Ollama (local, OpenAI-compatible) ─────────────────────────────────────
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel   { get; set; } = "phi3:mini";

    // ── Shared ────────────────────────────────────────────────────────────────
    public float  Temperature   { get; set; } = 0.7f;
    public int    MaxTokens     { get; set; } = 2048;
    public bool   DemoMode      { get; set; } = false;
}
