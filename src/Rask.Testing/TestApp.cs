using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Rask.Core;

namespace Rask.Testing;

// The app under test, booted from its own Program.cs: the project the test references whose entry point runs a Rask
// host. Each boot is a fresh one — its own services and its own database files — so one test's rows never show up in
// another's, and tests run side by side.
internal static class TestApp
{
    // Long enough for a cold first boot (JIT, migrations) on a loaded machine; a hang past it is a Program.cs that
    // never reaches its host, and the test says so instead of stalling the run.
    private static readonly TimeSpan StartupLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(10);

    // Whatever a test run did not shut down itself (a visit outside a test hook) goes when the process does.
    private static readonly ConcurrentBag<Booted> Left = [];

    // Looked up once: the test project's references do not change while it runs.
    private static readonly Lazy<MethodInfo?> Entry = new(FindEntryPoint);

    static TestApp() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        while (Left.TryTake(out var booted))
        {
            booted.Dispose();
        }
    };

    /// <summary>The app, started, the thread its Program.cs runs on, and the database folder that is its own.</summary>
    internal sealed class Booted(IServiceProvider services, Action stop, Thread program, string folder) : IDisposable
    {
        private int _disposed;

        public IServiceProvider Services { get; } = services;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // The app's own shutdown: its Run returns, and disposes it, on the thread it runs on.
            stop();
            program.Join(ShutdownLimit);
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file the OS still holds is left to the temp folder's own cleanup.
            }
        }
    }

    /// <summary>A freshly booted app, or <c>null</c> when no referenced app runs a Rask host.</summary>
    /// <param name="untilExit">Keep it until the process exits, rather than until whoever booted it disposes it.</param>
    /// <exception cref="InvalidOperationException">The app's Program.cs never started its host.</exception>
    internal static Booted? Boot(bool untilExit = false)
    {
        if (Entry.Value is not { } entry)
        {
            return null;
        }

        var folder = Directory.CreateTempSubdirectory("rask-test-").FullName;
        string[] args =
        [
            "--environment=Test",
            $"--Rask:ConnectionStrings:App=Data Source={Path.Combine(folder, "app.db")}",
            $"--Rask:ConnectionStrings:Logs=Data Source={Path.Combine(folder, "logs.db")}",
        ];

        // Program.cs runs as it does in production — to its Run, which blocks until the app stops — so it gets a thread
        // of its own. The capture flows to it with the execution context; the host hands the started app back here.
        var capture = AppCapture.Begin();
        Thread program;
        try
        {
            program = new Thread(() => RunProgram(entry, args, capture)) { IsBackground = true, Name = "Rask test app" };
            program.Start();
        }
        finally
        {
            AppCapture.End();
        }

        if (!capture.Wait(StartupLimit))
        {
            throw new InvalidOperationException(
                $"The app's Program.cs did not start within {StartupLimit.TotalSeconds:0} s. Page.Visit runs it as "
                + "written, up to its host's Run — check nothing before that waits for input.");
        }

        if (capture.Error is { } error)
        {
            Directory.Delete(folder, recursive: true);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }

        if (capture.Services is not { } services || capture.Stop is not { } stop)
        {
            // Program.cs ended without starting a Rask host — nothing to visit.
            Directory.Delete(folder, recursive: true);
            return null;
        }

        var booted = new Booted(services, stop, program, folder);
        if (untilExit)
        {
            Left.Add(booted);
        }

        return booted;
    }

    private static void RunProgram(MethodInfo entry, string[] args, AppCapture.Capture capture)
    {
        try
        {
            var result = entry.Invoke(null, entry.GetParameters().Length == 0 ? [] : [args]);
            if (result is Task running)
            {
                running.GetAwaiter().GetResult();
            }

            capture.Exited();
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            capture.Exited(e.InnerException);
        }
#pragma warning disable CA1031 // whatever Program.cs throws is the test's failure to report, on the test's thread
        catch (Exception e)
#pragma warning restore CA1031
        {
            capture.Exited(e);
        }
    }

    // The app is the project the test references whose entry point builds a Rask host. Found through the test's own
    // deps file rather than the loaded assemblies: a test that only calls Page.Visit("/") names no type of the app,
    // so nothing has loaded it yet. The test project's own entry point (the test SDK's) references no host, and a
    // test project that compiles the app's sources in (the wasm template) references none either — both fall back
    // to rendering over the services the test passes.
    private static MethodInfo? FindEntryPoint() =>
        ProjectReferences()
            .Select(static name => TryLoad(name))
            .Where(static a => a is { EntryPoint: not null } &&
                               a.GetReferencedAssemblies().Any(static r =>
                                   string.Equals(r.Name, "Rask.Server", StringComparison.Ordinal)))
            .Select(static a => a!.EntryPoint)
            .FirstOrDefault();

    private static IEnumerable<string> ProjectReferences()
    {
        if (AppContext.GetData("APP_CONTEXT_DEPS_FILES") is not string files)
        {
            yield break;
        }

        foreach (var file in files.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(file))
            {
                continue;
            }

            using var deps = JsonDocument.Parse(File.ReadAllText(file));
            if (!deps.RootElement.TryGetProperty("libraries", out var libraries))
            {
                continue;
            }

            foreach (var library in libraries.EnumerateObject())
            {
                if (library.Value.TryGetProperty("type", out var type) &&
                    string.Equals(type.GetString(), "project", StringComparison.Ordinal))
                {
                    yield return library.Name.Split('/', 2)[0];
                }
            }
        }
    }

    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (IOException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
