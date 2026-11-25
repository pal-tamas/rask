using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Testing;

/// <summary>
///     One test's own app: booted from the app's <c>Program.cs</c> the first time the test needs it — its first
///     <c>Accounts.Create&lt;User&gt;(…)</c>, its first <c>Page.Visit</c> — with its own database, and shut down when the test ends.
/// </summary>
/// <remarks>
///     Machinery: Rask.Testing's build adds a test-framework hook that calls <see cref="Begin()" /> before every
///     test and <see cref="End" /> after it, so a test writes neither. It is why
///     <c>await Accounts.Create&lt;User&gt;(…); var admin = await User.Where(…).First(); var page = Page.Visit("/x").As(admin);</c>
///     works in that order.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TestRun
{
    private static readonly AsyncLocal<Run?> Open = new();
    private static readonly AsyncLocal<IDisposable?> FixtureBinding = new();
    private static readonly AsyncLocal<Ambient.TokenScope?> TestCancellation = new();

    internal static Run? Current => Open.Value;

    /// <summary>Opens this test's app — not booted until something needs it.</summary>
    public static void Begin()
    {
        var run = new Run();
        Open.Value = run;

        // The test's static calls (Product.Create, Jobs.Enqueue) resolve through here, so the first one that needs the app boots it.
        _ = Ambient.Enter(run);
    }

    /// <summary>
    ///     Opens this test's app as <see cref="Begin()" /> does, and binds the model surface to the <c>TestDatabase</c> one of
    ///     <paramref name="fixtures" /> — the test class's class and collection fixtures — started, if one did.
    /// </summary>
    /// <remarks>
    ///     A fixture's <c>InitializeAsync</c> runs in a flow of the framework's own, so a database it starts reaches no test
    ///     by itself; bound here, before each test and in the test's own flow, every test of the class reads and writes it —
    ///     beside the fixtures of other classes running in parallel. A fixture that is a <c>TestDatabase</c>, or has one
    ///     as a public property, counts; the first found is bound. A database the test starts itself still wins.
    /// </remarks>
    /// <param name="fixtures">The fixtures the test class was given.</param>
    public static void Begin(IEnumerable<object> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        Begin();
        FixtureBinding.Value = FixtureDatabase.Bind(fixtures);
    }

    /// <summary>
    ///     Opens this test's app as <see cref="Begin(IEnumerable{object})" /> does, and makes <paramref name="cancellation" />
    ///     the test's ambient one — so every Rask call the test makes without a token, <c>await Product.Create(model)</c>,
    ///     <c>Order.Where(…)</c>, <c>TestDatabase.Start()</c>, is cancelled with the test.
    /// </summary>
    /// <remarks>
    ///     The xUnit v3 hook passes <c>TestContext.Current.CancellationToken</c>. A token the test passes itself still
    ///     wins, and a render still sets the test's aside for the lifetimes of what it mounts.
    /// </remarks>
    /// <param name="fixtures">The fixtures the test class was given.</param>
    /// <param name="cancellation">The test's cancellation: the run was aborted, or the test timed out.</param>
    public static void Begin(IEnumerable<object> fixtures, CancellationToken cancellation)
    {
        Begin(fixtures);
        TestCancellation.Value = Ambient.Enter(cancellation);
    }

    /// <summary>Shuts down the app this test booted, if it booted one.</summary>
    public static void End()
    {
        if (TestCancellation.Value is { } cancellation)
        {
            TestCancellation.Value = null;
            cancellation.Dispose();
        }

        if (FixtureBinding.Value is { } binding)
        {
            FixtureBinding.Value = null;
            binding.Dispose();
        }

        if (Open.Value is { } run)
        {
            Open.Value = null;
            run.Dispose();
        }
    }

    /// <summary>A test's app, booted on first use; its services are a scope of it, open for the whole test.</summary>
    /// <remarks>
    ///     Ambient for the whole test, but booted only by a call with nothing else to go on — <c>Page.Visit</c>, a
    ///     battery's static, a read with no database configured. A lookup with another answer never boots it, and a
    ///     test that configures its own database (<c>TestDatabase.Start(…)</c>) keeps it from booting at all.
    /// </remarks>
    internal sealed class Run : OnDemandServices, IDisposable
    {
        private readonly Lock _gate = new();
        private TestApp.Booted? _booted;
        private IServiceScope? _scope;
        private bool _tried;

        /// <summary>The app's scope, booting the app if it has not; <c>null</c> when the test project references no app.</summary>
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
                        Volatile.Write(ref _scope, _booted?.Services.CreateScope());
                    }

                    return _scope?.ServiceProvider;
                }
            }
        }

        internal override IServiceProvider? Started => Volatile.Read(ref _scope)?.ServiceProvider;

        public void Dispose()
        {
            _scope?.Dispose();
            _booted?.Dispose();
        }

        protected override IServiceProvider? StartNow() => Services;
    }
}
