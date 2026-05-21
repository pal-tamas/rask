namespace Rask.Example.Shared.Tests.Infrastructure;

internal static class WaitFor
{
    public static async Task True(Func<bool> condition, TimeSpan timeout, string? reason = null)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }

        if (reason is not null)
        {
            throw new TimeoutException($"WaitFor timed out after {timeout}: {reason}");
        }
    }
}
