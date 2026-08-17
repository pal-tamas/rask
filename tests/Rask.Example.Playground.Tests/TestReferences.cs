using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Rask.Example.Playground.Tests;

// The desktop stand-in for the browser host downloading _framework/*.dll: the shared-framework BCL from the
// trusted-platform set plus Rask.Core (where Component and the Generated.* factories live). Shared by the
// compiler and workspace tests so both exercise the pipeline against the same reference set.
internal static class TestReferences
{
    public static ImmutableArray<MetadataReference> Build()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var refs = trusted
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        refs.Add(MetadataReference.CreateFromFile(Assembly.Load("Rask.Core").Location));
        return refs.ToImmutableArray();
    }
}
