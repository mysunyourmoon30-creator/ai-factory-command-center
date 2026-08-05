using AI.Factory.Core.Copilot;

namespace AI.Factory.IntegrationTests;

/// <summary>
/// Swapped in for the real Ollama HTTP client (see AiFactoryWebApplicationFactory), the same way
/// TimeProvider and the DbContext provider are already swapped for every integration test. Tests
/// configure behavior via the static fields before calling the endpoint under test; xUnit runs
/// tests within one class sequentially, so this is safe without extra synchronization.
/// </summary>
public sealed class FakeOllamaClient : IOllamaClient
{
    public static Func<string, string, string>? NextRawResponse { get; set; }
    public static Exception? NextException { get; set; }
    public static bool NextReachable { get; set; } = true;
    public static int CallCount { get; private set; }

    public Task<string> CompleteAsync(string systemPrompt, string userContent, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (NextException is { } exception)
        {
            NextException = null;
            throw exception;
        }

        return Task.FromResult(NextRawResponse?.Invoke(systemPrompt, userContent) ?? "{}");
    }

    public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default) => Task.FromResult(NextReachable);

    public static void Reset()
    {
        NextRawResponse = null;
        NextException = null;
        NextReachable = true;
        CallCount = 0;
    }
}
