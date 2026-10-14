using System.Globalization;
using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The page's processor slowed to a fraction of this machine's, when <c>RASK_E2E_CPU_SLOWDOWN</c> says by how
///     much: a journey that passes here and fails on a two-core runner is run as that runner runs it.
/// </summary>
/// <remarks>
///     <c>RASK_E2E_CPU_SLOWDOWN=20 dotnet test … --filter …</c> gives every page a twentieth of a core
///     (Chromium's <c>Emulation.setCPUThrottlingRate</c>). Unset, nothing is slowed.
/// </remarks>
internal static class SlowMachine
{
    /// <summary>Slows <paramref name="page" /> by the factor the environment names, before it loads anything.</summary>
    public static async Task ApplyAsync(IPage page)
    {
        if (!double.TryParse(Environment.GetEnvironmentVariable("RASK_E2E_CPU_SLOWDOWN"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
            || rate <= 1)
        {
            return;
        }

        var session = await page.Context.NewCDPSessionAsync(page);
        await session.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = rate });
    }
}
