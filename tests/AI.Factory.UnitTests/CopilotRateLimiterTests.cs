using AI.Factory.Infrastructure.Copilot;

namespace AI.Factory.UnitTests;

/// <summary>
/// Unit tests rather than integration ones: the integration host deliberately raises
/// RateLimits:AiCopilotPermitLimit to 1000 so that neither this limiter nor the endpoint policy it
/// mirrors interferes with a test class asking thirteen questions against a frozen clock. The
/// algorithm therefore has to be proven here, where the clock can be moved on demand.
/// </summary>
public sealed class CopilotRateLimiterTests
{
    private sealed class MovableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now = now.Add(by);
    }

    private static readonly DateTimeOffset T = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Permits_exactly_the_configured_number_of_questions_per_window()
    {
        var limiter = new CopilotRateLimiter(new MovableTimeProvider(T), permitLimit: 3);

        Assert.True(limiter.Check(1).Permitted);
        Assert.True(limiter.Check(1).Permitted);
        Assert.True(limiter.Check(1).Permitted);
        Assert.False(limiter.Check(1).Permitted);
    }

    [Fact]
    public void Only_the_first_rejection_in_a_window_is_flagged_for_logging()
    {
        var limiter = new CopilotRateLimiter(new MovableTimeProvider(T), permitLimit: 1);

        limiter.Check(1);

        Assert.True(limiter.Check(1).IsFirstRejection);
        Assert.False(limiter.Check(1).IsFirstRejection);
        Assert.False(limiter.Check(1).IsFirstRejection);
    }

    [Fact]
    public void Budget_is_per_user_so_one_caller_cannot_spend_anothers()
    {
        var limiter = new CopilotRateLimiter(new MovableTimeProvider(T), permitLimit: 1);

        Assert.True(limiter.Check(1).Permitted);
        Assert.False(limiter.Check(1).Permitted);
        Assert.True(limiter.Check(2).Permitted);
    }

    [Fact]
    public void Budget_refills_once_the_window_has_passed()
    {
        var clock = new MovableTimeProvider(T);
        var limiter = new CopilotRateLimiter(clock, permitLimit: 1);

        Assert.True(limiter.Check(1).Permitted);
        Assert.False(limiter.Check(1).Permitted);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(limiter.Check(1).Permitted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.Check(1).Permitted);
    }
}
