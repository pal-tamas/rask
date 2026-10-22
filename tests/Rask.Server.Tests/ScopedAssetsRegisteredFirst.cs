using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyFixture(typeof(Rask.Server.Tests.ScopedAssetsRegisteredFirst))]

namespace Rask.Server.Tests;

/// <summary>
///     Registers every library's scoped CSS and TypeScript before the first test runs.
/// </summary>
/// <remarks>
///     The registry is one per process, and a library fills it from a module initializer — which runs when a test
///     first touches that library (<c>Rask.Ui</c>'s editor, say), at whatever moment that is. Every live page's
///     <c>&lt;head&gt;</c> names the bundles, so a page that rendered before that moment and renders again after it
///     sends a frame its own state never asked for: a test counting the frames before an ack saw one too many.
/// </remarks>
public sealed class ScopedAssetsRegisteredFirst
{
    private static readonly string[] Registrations = ["__RaskScopedCssRegistration", "__RaskScopedJsRegistration"];

    public ScopedAssetsRegisteredFirst()
    {
        foreach (var assembly in RaskAssemblies(typeof(ScopedAssetsRegisteredFirst).Assembly))
        {
            if (Registrations.Any(name => assembly.GetType(name, throwOnError: false) is not null))
            {
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            }
        }
    }

    // This assembly and every Rask assembly it reaches, each once.
    private static IEnumerable<Assembly> RaskAssemblies(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.GetName().Name! };
        var pending = new Queue<Assembly>([root]);
        while (pending.TryDequeue(out var assembly))
        {
            yield return assembly;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is { } name && name.StartsWith("Rask", StringComparison.Ordinal) && seen.Add(name))
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
            }
        }
    }
}
