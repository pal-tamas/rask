using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Testing;

/// <summary>
///     One test's own app: booted from the app's <c>Program.cs</c> the first time the test needs it — its first
///     <c>User.Create(…)</c>, its first <c>Page.Visit</c> — with its own database, and shut down when the test ends.
/// </summary>
/// <remarks>
///     Machinery: Rask.Testing's build adds a test-framework hook that calls <see cref="Begin" /> before every
///     test and <see cref="End" /> after it, so a test writes neither. It is why
///     <c>var admin = await User.Create(…); var page = Page.Visit("/x").As(admin);</c> works in that order.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TestRun
{
    private static readonly AsyncLocal<Run?> Open = new();

    internal static Run? Current => Open.Value;

    /// <summary>Opens this test's app — not booted until something uses it.</summary>
    public static void Begin()
    {
        var run = new Run();
        Open.Value = run;

        // The test's static calls (Product.Create, Jobs.Enqueue) resolve through here, so the first one boots the app.
        _ = Ambient.Enter(run);
    }

    /// <summary>Shuts down the app this test booted, if it booted one.</summary>
    public static void End()
    {
        if (Open.Value is { } run)
        {
            Open.Value = null;
            run.Dispose();
        }
    }

    /// <summary>A test's app, booted on first use; its services are a scope of it, open for the whole test.</summary>
    internal sealed class Run : IServiceProvider, IDisposable
    {
        private readonly Lock _gate = new();
        private TestApp.Booted? _booted;
        private IServiceScope? _scope;
        private bool _tried;

        /// <summary>The app's scope, or <c>null</c> when the test project references no app.</summary>
        public IServiceProvider? Services
        {
            get
            {
                lock (_gate)
                {
                    if (!_tried)
                    {
                        _tried = true;
                        _booted = TestApp.Boot();
                        _scope = _booted?.Services.CreateScope();
                    }

                    return _scope?.ServiceProvider;
                }
            }
        }

        public object? GetService(Type serviceType) => Services?.GetService(serviceType);

        public void Dispose()
        {
            _scope?.Dispose();
            _booted?.Dispose();
        }
    }
}
