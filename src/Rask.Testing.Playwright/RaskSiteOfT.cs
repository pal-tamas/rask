namespace Rask.Testing;

/// <summary>
///     The app <typeparamref name="TProgram" /> is in, running for browser tests: <c>new RaskSite&lt;Program&gt;()</c> in a
///     test project that references the app.
/// </summary>
/// <remarks>
///     <para>
///         It runs the app project's own build from the app's own folder, the way <c>dotnet run</c> would — the test
///         project's build records where that is (Rask.Testing.Playwright's build adds it). Built with the tests, so the
///         app a test runs is never an older build than the code.
///     </para>
///     <para>
///         One public constructor and no arguments, so a test framework can make it a fixture as it is. An app that sets
///         its site up derives from it:
///         <c>public sealed class Site : RaskSite&lt;Program&gt; { public Site() =&gt; Health = "/version"; }</c>
///     </para>
/// </remarks>
/// <typeparam name="TProgram">A type in the app — its <c>Program</c>.</typeparam>
public class RaskSite<TProgram>() : RaskSite(AppBuild.Of(typeof(TProgram).Assembly)) where TProgram : class;
