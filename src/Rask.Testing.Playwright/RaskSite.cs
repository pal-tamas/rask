using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Playwright;

namespace Rask.Testing;

/// <summary>
///     A Rask app, running for browser tests: its own build as a process of its own, on a free port, over a database of
///     its own — and one browser to open its pages in. Usually <see cref="RaskSite{TProgram}" />, for the app the test
///     project references.
/// </summary>
/// <remarks>
///     <para>
///         Nothing starts until a test needs it: the first <see cref="Walk" />, <see cref="SignedIn" />,
///         <see cref="SignedOut" /> or <see cref="Start" /> starts the app and waits for its health check; the first page
///         launches Chromium (installing it once). Disposing the site ends the app and every process it started, closes the
///         browser and deletes the run's <see cref="Folder" />.
///     </para>
///     <para>
///         The app runs with <c>ASPNETCORE_ENVIRONMENT=Testing</c>, and its database and log store as SQLite files in
///         <see cref="Folder" /> (<c>Rask__ConnectionStrings__App</c>, <c>Rask__ConnectionStrings__Logs</c>). Change or
///         remove any of it in <see cref="Environment" /> — an app on SQL Server names its test database there.
///     </para>
///     <para>
///         Set up before it starts — <see cref="Environment" />, <see cref="SignIn" />, <see cref="Health" /> — in a derived
///         class's constructor or an object initializer. Independent of the test framework; on xUnit v3 one site serves the
///         whole assembly: <c>[assembly: AssemblyFixture(typeof(RaskSite&lt;Program&gt;))]</c>, and a test takes
///         <c>RaskSite&lt;Program&gt; site</c>.
///     </para>
/// </remarks>
public class RaskSite : IAsyncDisposable
{
    private const int PhoneHeight = 844;

    // Run in the page at phone width once it has settled, after two frames so a layout the resize set off is in. Names the
    // element reaching furthest right, which is the one to fix far more often than the page.
    private const string Overflow = """
        () => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => {
            const root = document.documentElement;
            const width = root.clientWidth;
            if (root.scrollWidth <= width) return done(null);
            let widest = null, right = width;
            for (const element of document.body.querySelectorAll('*')) {
                const box = element.getBoundingClientRect();
                if (box.right > right + 0.5) { right = box.right; widest = element; }
            }
            const name = widest === null ? 'the page' : widest.tagName.toLowerCase()
                + (widest.id ? '#' + widest.id : '')
                + [...widest.classList].slice(0, 3).map(c => '.' + c).join('');
            done('wider than a phone: ' + root.scrollWidth + ' px across ' + width + ' px, widest is ' + name
                + ' (' + Math.round(right) + ' px)');
        })))
        """;

    private readonly AppBuild _build;
    private readonly string _firstRunToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    private readonly Lazy<Task> _started;
    private readonly Lazy<Task<(IPlaywright Playwright, IBrowser Browser)>> _browser;
    private readonly Dictionary<string, string> _signedIn = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signingIn = new(1, 1);
    private HostProcess? _host;
    private Uri? _address;

    /// <summary>
    ///     The app built (or published) at <paramref name="host" />, run from that folder — for an app the test project
    ///     does not reference. Set the site up before it starts, in an object initializer or a derived class's constructor.
    /// </summary>
    /// <param name="host">The app's <c>App.dll</c>.</param>
    public RaskSite(string host) : this(AppBuild.At(host))
    {
    }

    private protected RaskSite(AppBuild build)
    {
        _build = build;
        ContentRoot = build.ContentRoot;
        Folder = Directory.CreateTempSubdirectory("rask-site-").FullName;
        Password = "walk-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Testing",
            ["Rask__ConnectionStrings__App"] = $"Data Source={Path.Combine(Folder, "app.db")}",
            ["Rask__ConnectionStrings__Logs"] = $"Data Source={Path.Combine(Folder, "logs.db")}",
            ["Rask__Mail__PickupDirectory"] = Path.Combine(Folder, "mail"),
            ["Rask__Auth__FirstRunToken"] = _firstRunToken,
        };
        _started = new Lazy<Task>(() => Task.Run(Starting));
        _browser = new Lazy<Task<(IPlaywright, IBrowser)>>(() => Task.Run(Launching));
    }

    /// <summary>The app's built assembly, which <c>dotnet</c> runs.</summary>
    public string Host => _build.Host;

    /// <summary>The folder the app runs in — its <c>appsettings.json</c> and <c>wwwroot</c>: the app's project folder for a build.</summary>
    public string ContentRoot { get; set; }

    /// <summary>
    ///     The app's environment variables, over this process's own. A <c>null</c> value removes one. ASP.NET reads
    ///     <c>Rask__Section__Key</c> as the setting <c>Rask:Section:Key</c>.
    /// </summary>
    public IDictionary<string, string?> Environment { get; }

    /// <summary>The path the site waits on until it answers with a success status. A <c>RaskApp</c> answers at <c>/health</c>.</summary>
    public string Health { get; set; } = "/health";

    /// <summary>How long the app may take to answer <see cref="Health" />: three minutes, for a cold start on a busy machine.</summary>
    public TimeSpan StartWithin { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    ///     The administrator's name. Signing in through Rask.Auth (no <see cref="SignIn" /> of the app's own) — its API, or
    ///     the <c>/login</c> and <c>/register</c> pages <c>rask new</c> writes — the site registers this account first, with
    ///     the app's one-time token, so it is the account that claims the app.
    /// </summary>
    public string Admin { get; set; } = "admin@example.com";

    /// <summary>The password every account the site signs in with uses: a new one each run, unless the app's test data says otherwise.</summary>
    public string Password { get; set; }

    /// <summary>
    ///     How the app signs a person in, for an app that signs in neither through Rask.Auth's API nor through the pages
    ///     <c>rask new</c> writes: the page is blank, and the sign-in is done once it returns. <c>site.SignIn = async (page, name) => { await page.GotoAsync("/login"); … };</c>
    /// </summary>
    public Func<IPage, string, Task>? SignIn { get; set; }

    /// <summary>
    ///     What every browser context the site opens starts from — the viewport, the locale, the time zone:
    ///     <c>site.Contexts.Locale = "hu-HU";</c>
    /// </summary>
    public BrowserNewContextOptions Contexts { get; } = new();

    /// <summary>
    ///     Where the app sends a visitor to sign in: <c>/login</c>, as Rask.Auth does. A walk signed in that lands there
    ///     fails — the sign-in did not hold.
    /// </summary>
    public string SignInPage { get; set; } = "/login";

    /// <summary>The width a walk checks the page fits, in CSS pixels: 390, a phone held upright.</summary>
    public int PhoneWidth { get; set; } = 390;

    /// <summary>The run's own folder: the app's database files and mail. Deleted with the site.</summary>
    public string Folder { get; }

    /// <summary>Where the app listens, <c>http://localhost:{port}/</c>. Every page the site opens resolves a relative URL against it.</summary>
    /// <exception cref="InvalidOperationException">The app has not started yet.</exception>
    public Uri Address => _address ?? throw new InvalidOperationException(
        "The site has not started yet: await site.Start() first — or site.Walk(…), site.SignedIn(…) or site.SignedOut(), which start it.");

    /// <summary>Everything the app has written so far, standard output and error together.</summary>
    public string Log => _host?.Output ?? "";

    /// <summary>The pages of the app <typeparamref name="TProgram" /> is in, for a test that walks every one.</summary>
    /// <typeparam name="TProgram">A type in the app — its <c>Program</c>.</typeparam>
    /// <returns>Every page its route table opens without a value, in path order.</returns>
    public static SitePages Pages<TProgram>() where TProgram : class => SitePages.Of(typeof(TProgram).Assembly);

    /// <summary>Whether the app has written <paramref name="text" /> since it started — a warning it should have given.</summary>
    /// <param name="text">The text to look for, as written.</param>
    /// <returns><c>true</c> when a line holds it.</returns>
    public bool Said(string text) => _host?.Said(text) ?? false;

    /// <summary>Starts the app, once, and waits until it answers <see cref="Health" />. Every page the site opens calls it.</summary>
    /// <returns>A task that completes once the app answers.</returns>
    /// <exception cref="InvalidOperationException">The app stopped as it started; the message holds what it said.</exception>
    /// <exception cref="TimeoutException">It did not answer within <see cref="StartWithin" />.</exception>
    public Task Start() => _started.Value;

    /// <summary>A new page, signed out, in a browser context of its own.</summary>
    /// <remarks>A test that opens many closes each when done — <c>await page.Context.CloseAsync()</c>; the rest close with the browser.</remarks>
    /// <returns>The page, still blank: <c>await page.GotoAsync("/orders")</c>.</returns>
    public async Task<IPage> SignedOut()
    {
        var context = await NewContext(null).ConfigureAwait(false);
        return await context.NewPageAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     A new page signed in as <paramref name="name" />, in a browser context of its own. The first one signs in; every
    ///     later one starts from that sign-in's cookies.
    /// </summary>
    /// <remarks>A test that opens many closes each when done — <c>await page.Context.CloseAsync()</c>; the rest close with the browser.</remarks>
    /// <param name="name">Who: an account's name, or <see cref="Admin" />.</param>
    /// <returns>The page, still blank: <c>await page.GotoAsync("/orders")</c>.</returns>
    public async Task<IPage> SignedIn(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var state = await StateOf(name).ConfigureAwait(false);
        var context = await NewContext(state).ConfigureAwait(false);
        return await context.NewPageAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Opens <paramref name="path" /> and fails on anything wrong with it: an answer that is not a success (the
    ///     not-found page, the forbidden one), a console or page error, a request to the app that failed or answered 400 or
    ///     worse, a connection opened twice or closed, the error overlay, and a page wider than <see cref="PhoneWidth" />.
    /// </summary>
    /// <param name="path">The page, from the app's root: <c>"/orders"</c>.</param>
    /// <returns>The walk, run when awaited — signed out, or <c>.As(name)</c>.</returns>
    public PageWalk Walk(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new PageWalk(user => Walking(path, user), null);
    }

    /// <summary>
    ///     Watches <paramref name="page" /> from now on for a console error, an error it throws, and a request to the app that
    ///     failed or answered 400 or worse.
    /// </summary>
    /// <param name="page">A page the site opened.</param>
    /// <returns>What it has seen so far, growing as the page goes on.</returns>
    public PageProblems Watch(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var problems = new PageProblems();
        page.Console += (_, message) =>
        {
            // Chromium's own line for a failed load names no address; the response below names it, so it is said once.
            if (string.Equals(message.Type, "error", StringComparison.Ordinal)
                && !message.Text.StartsWith("Failed to load resource:", StringComparison.Ordinal))
            {
                problems.Add($"console error: {message.Text}");
            }
        };
        page.PageError += (_, error) => problems.Add($"page error: {error}");
        page.RequestFailed += (_, request) =>
        {
            if (IsTheApps(request.Url))
            {
                problems.Add($"{request.Method} {PathOf(request.Url)} failed: {request.Failure}");
            }
        };
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && IsTheApps(response.Url))
            {
                problems.Add($"{response.Request.Method} {PathOf(response.Url)} answered {response.Status} {response.StatusText}");
            }
        };
        return problems;
    }

    /// <summary>Ends the app and every process it started, closes the browser, and deletes <see cref="Folder" />.</summary>
    /// <returns>A task that completes once all of it is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await Closing().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task Closing()
    {
        if (_browser.IsValueCreated && _browser.Value.IsCompletedSuccessfully)
        {
            var (playwright, browser) = await _browser.Value.ConfigureAwait(false);
            await browser.CloseAsync().ConfigureAwait(false);
            playwright.Dispose();
        }

        if (_host is { } host)
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }

        _signingIn.Dispose();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // a file the OS still holds: the temp folder's own cleanup takes it
        }
    }

    private async Task Starting()
    {
        if (!File.Exists(Host))
        {
            throw new InvalidOperationException(
                $"The app's build is not there: {Host}. Build the app first — a test project that references it builds it "
                + "with the tests.");
        }

        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var address = new Uri($"http://localhost:{LoopbackPort.Reserve()}/");
            var host = HostProcess.Start(StartInfo(address));
            if (await host.Answering(new Uri(address, Health), StartWithin).ConfigureAwait(false))
            {
                _host = host;
                _address = address;
                return;
            }

            await host.DisposeAsync().ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"The app could not listen: {attempts} free ports in a row were taken before it could bind them. "
            + "Something on this machine takes ports as fast as they are handed out.");
    }

    private ProcessStartInfo StartInfo(Uri address)
    {
        // The dotnet the test run is on, when the SDK says which; the one on the PATH otherwise.
        var dotnet = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path && File.Exists(path)
            ? path
            : "dotnet";
        var start = new ProcessStartInfo(dotnet) { WorkingDirectory = ContentRoot };
        start.ArgumentList.Add(Host);
        foreach (var (key, value) in Environment)
        {
            if (value is null)
            {
                start.Environment.Remove(key);
            }
            else
            {
                start.Environment[key] = value;
            }
        }

        start.Environment["ASPNETCORE_URLS"] = address.GetLeftPart(UriPartial.Authority);
        return start;
    }

    private static async Task<(IPlaywright, IBrowser)> Launching()
    {
        // Installs Chromium the first time on a machine; a no-op once it is there.
        var installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (installed != 0)
        {
            throw new InvalidOperationException($"playwright install chromium exited with code {installed}.");
        }

        var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        var browser = await playwright.Chromium.LaunchAsync().ConfigureAwait(false);
        return (playwright, browser);
    }

    private async Task<IBrowserContext> NewContext(string? state)
    {
        await Start().ConfigureAwait(false);
        var (_, browser) = await _browser.Value.ConfigureAwait(false);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions(Contexts)
        {
            BaseURL = Address.ToString(),
            StorageState = state,
        }).ConfigureAwait(false);
        return context;
    }

    private async Task<string> StateOf(string name)
    {
        await _signingIn.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SignedInState(name).ConfigureAwait(false);
        }
        finally
        {
            _signingIn.Release();
        }
    }

    // Under _signingIn. The administrator claims the app before anyone else registers through Rask.Auth.
    private async Task<string> SignedInState(string name)
    {
        if (_signedIn.TryGetValue(name, out var known))
        {
            return known;
        }

        if (SignIn is null && !string.Equals(name, Admin, StringComparison.Ordinal))
        {
            await SignedInState(Admin).ConfigureAwait(false);
        }

        var context = await NewContext(null).ConfigureAwait(false);
        try
        {
            var page = await context.NewPageAsync().ConfigureAwait(false);
            if (SignIn is { } signIn)
            {
                await signIn(page, name).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await RaskAuthSignIn.SignIn(page, Health, name, Password, _firstRunToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException failed)
                {
                    throw new InvalidOperationException(
                        $"{failed.Message}{System.Environment.NewLine}The app said, last:{System.Environment.NewLine}{LastSaid()}", failed);
                }
            }

            var state = await context.StorageStateAsync().ConfigureAwait(false);
            _signedIn[name] = state;
            return state;
        }
        finally
        {
            await context.CloseAsync().ConfigureAwait(false);
        }
    }

    private async Task Walking(string path, string? user)
    {
        var page = user is null ? await SignedOut().ConfigureAwait(false) : await SignedIn(user).ConfigureAwait(false);
        try
        {
            var problems = Watch(page);
            var connections = 0;
            page.WebSocket += (_, socket) =>
            {
                if (IsTheApps(socket.Url))
                {
                    Interlocked.Increment(ref connections);
                    socket.Close += (_, _) => problems.Add("the connection to the app closed");
                }
            };

            await page.GotoAsync(path).ConfigureAwait(false);
            await page.WaitForRask().ConfigureAwait(false);
            if (user is not null && new Uri(page.Url).AbsolutePath.StartsWith(SignInPage, StringComparison.Ordinal)
                && !path.StartsWith(SignInPage, StringComparison.Ordinal))
            {
                problems.Add($"{user} was sent to sign in ({PathOf(page.Url)}): the sign-in did not hold");
            }

            if (await page.Locator(".rask-overlay[data-show]").CountAsync().ConfigureAwait(false) > 0)
            {
                problems.Add("the error overlay is showing");
            }

            if (Volatile.Read(ref connections) > 1)
            {
                problems.Add($"the connection to the app was opened {connections} times");
            }

            await page.SetViewportSizeAsync(PhoneWidth, PhoneHeight).ConfigureAwait(false);
            if (await page.EvaluateAsync<string?>(Overflow).ConfigureAwait(false) is { } wide)
            {
                problems.Add(wide);
            }

            problems.Close();
            if (problems.Count > 0)
            {
                throw new WalkException(path, user, [.. problems], LastSaid());
            }
        }
        finally
        {
            await page.Context.CloseAsync().ConfigureAwait(false);
        }
    }

    // The end of what the app wrote: where the reason for a failure is, without the whole run's log.
    private string LastSaid() => string.Join(System.Environment.NewLine, Log.ReplaceLineEndings("\n").Split('\n').TakeLast(40));

    private bool IsTheApps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && string.Equals(uri.Authority, Address.Authority, StringComparison.OrdinalIgnoreCase);

    private static string PathOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : url;
}
