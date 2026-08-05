using AI.Factory.Core.Copilot;
using AI.Factory.Infrastructure.Persistence;

namespace AI.Factory.Infrastructure.Copilot;

public sealed class ReadinessService(AppDbContext dbContext, IOllamaClient ollamaClient) : IReadinessService
{
    public async Task<ReadinessDto> CheckAsync(CancellationToken cancellationToken = default)
    {
        var sqlHealthy = await dbContext.Database.CanConnectAsync(cancellationToken);
        var ollamaHealthy = await ollamaClient.IsReachableAsync(cancellationToken);

        return new ReadinessDto(
            Healthy: sqlHealthy && ollamaHealthy,
            Application: "Healthy",
            Sql: sqlHealthy ? "Healthy" : "Unhealthy",
            Ollama: ollamaHealthy ? "Healthy" : "Unhealthy");
    }
}
