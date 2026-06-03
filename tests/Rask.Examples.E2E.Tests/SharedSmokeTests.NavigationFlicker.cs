// Intentionally empty after refactor — the LiveTicker-based nav-flicker tests
// moved to ExampleSmokeTests.NavigationFlicker.cs because StandaloneWasm's
// NavigateToAsync waits for window.location to match /realtime/{Symbol},
// which is timing-flaky on WasmAppHost. Kept as a placeholder so the future
// addition of cross-host nav-flicker assertions has an obvious home.
namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
}
