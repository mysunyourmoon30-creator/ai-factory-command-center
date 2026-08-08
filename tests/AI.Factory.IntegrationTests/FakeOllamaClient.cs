using AI.Factory.Core.Copilot;

namespace AI.Factory.IntegrationTests;

/// <summary>
/// Swapped in for the real Ollama HTTP client (see AiFactoryWebApplicationFactory), the same way
/// TimeProvider and the DbContext provider are already swapped for every integration test.
/// <para>
/// State is per-instance, and the instance is a singleton of one test host, so each test class
/// gets its own. It used to be static, on the reasoning that xUnit runs the tests inside a class
/// sequentially - true, but irrelevant, because xUnit runs test *classes* in parallel and both
/// CopilotTests and ReadinessTests drive this fake. ReadinessTests calling Reset() could land
/// between CopilotTests setting NextRawResponse and the request reaching the fake, which made
/// CopilotTests fail intermittently with "Ollama was not called" (finding A8).
/// </para>
/// </summary>
public sealed class FakeOllamaClient : IOllamaClient
{
    public Func<string, string, string>? NextRawResponse { get; set; }
    public Exception? NextException { get; set; }
    public bool NextReachable { get; set; } = true;
    public int CallCount { get; private set; }

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

    public void Reset()
    {
        NextRawResponse = null;
        NextException = null;
        NextReachable = true;
        CallCount = 0;
    }
}
