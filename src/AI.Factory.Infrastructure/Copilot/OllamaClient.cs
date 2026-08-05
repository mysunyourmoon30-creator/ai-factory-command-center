using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AI.Factory.Core.Copilot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AI.Factory.Infrastructure.Copilot;

/// <summary>
/// Thin HttpClient wrapper over Ollama's REST API (§5.6, §10.2). BaseAddress and the
/// localhost-only guard (§10.10) are enforced once at DI registration time, not here.
/// </summary>
public sealed class OllamaClient(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaClient> logger) : IOllamaClient
{
    private string Model => configuration["Ollama:Model"] ?? "qwen3:4b";

    public async Task<string> CompleteAsync(string systemPrompt, string userContent, CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest(Model, systemPrompt, userContent, "json", false);
        using var response = await httpClient.PostAsJsonAsync("/api/generate", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken);
        return payload?.Response ?? throw new InvalidOperationException("Ollama returned an empty response.");
    }

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await httpClient.GetAsync("/api/version", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ollama health ping failed.");
            return false;
        }
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaGenerateResponse([property: JsonPropertyName("response")] string Response);
}
