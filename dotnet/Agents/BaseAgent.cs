using System.Text.Json.Nodes;
using MultiAgentSupportAI.Models;

namespace MultiAgentSupportAI.Agents;

/// <summary>
/// Abstract base for all agents.
/// Uses ILlmService so the provider (Gemini / Groq / Ollama) is swappable via config.
/// </summary>
public abstract class BaseAgent
{
    protected readonly ILlmService  Llm;
    protected readonly AppSettings  Settings;
    protected readonly ILogger      Logger;

    protected BaseAgent(ILlmService llm, AppSettings settings, ILogger logger)
    {
        Llm      = llm;
        Settings = settings;
        Logger   = logger;
    }

    protected abstract string GetSystemPrompt();
    protected abstract List<GeminiFunctionDeclaration> GetAvailableTools();
    protected virtual Task<string> ExecuteToolAsync(string toolName, JsonObject toolInput)
        => Task.FromResult($"[Tool '{toolName}' not implemented by {GetType().Name}]");

    /// <summary>Runs the full agentic loop for one user message.</summary>
    protected async Task<AgentResult> RunAsync(string userMessage)
    {
        Logger.LogInformation("{Agent} run_start: {Message}", GetType().Name, userMessage[..Math.Min(100, userMessage.Length)]);

        var result = await Llm.RunAsync(
            systemPrompt:    GetSystemPrompt(),
            userMessage:     userMessage,
            toolDefinitions: GetAvailableTools(),
            toolExecutor:    ExecuteToolAsync
        );

        Logger.LogInformation("{Agent} run_complete: success={Success}", GetType().Name, result.Success);
        return result;
    }
}
