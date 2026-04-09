using System.Text.Json.Nodes;
using MultiAgentSupportAI.Models;

namespace MultiAgentSupportAI;

/// <summary>
/// Abstraction over any LLM backend (Gemini, Groq, Ollama).
/// Swap provider in appsettings.json without changing agent code.
/// </summary>
public interface ILlmService
{
    Task<AgentResult> RunAsync(
        string                                  systemPrompt,
        string                                  userMessage,
        List<GeminiFunctionDeclaration>?        toolDefinitions = null,
        Func<string, JsonObject, Task<string>>? toolExecutor    = null);

    /// <summary>Streams response tokens one chunk at a time.</summary>
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage);
}
