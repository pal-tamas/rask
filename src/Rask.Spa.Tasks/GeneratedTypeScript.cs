using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Rask.Spa.Tasks;

/// <summary>
///     Reads the TypeScript the CQRS generator left behind in a compiled assembly.
/// </summary>
/// <remarks>
///     <para>
///         A source generator cannot write files: it has no build directory of its own, and an
///         incremental run can be cancelled after it has produced half its output. So the TypeScript
///         travels as two string constants on an internal type, and this lifts them back out.
///     </para>
///     <para>
///         Read straight from the PE metadata rather than by loading the assembly. Loading is what
///         makes the WASM asset bake fragile — an MSBuild worker that has already loaded an assembly
///         of the same name throws, and node reuse makes that roughly one build in three (#650). A
///         constant is metadata; it does not need a runtime, a resolver, or the reference closure.
///     </para>
/// </remarks>
public static class GeneratedTypeScript
{
    private const string CqrsNamespace = "Rask.Cqrs.Generated";
    private const string CqrsTypeName = "RaskGeneratedTypeScript";

    /// <summary>
    ///     The constants on <c>Rask.Cqrs.Generated.RaskGeneratedTypeScript</c>, keyed by field name.
    /// </summary>
    /// <returns>
    ///     An empty dictionary when the type is absent, which is the ordinary state of an assembly
    ///     whose project never opted in — not an error.
    /// </returns>
    public static IReadOnlyDictionary<string, string> Read(string assemblyPath) =>
        Read(assemblyPath, CqrsNamespace, CqrsTypeName);

    /// <summary>
    ///     The constants on <paramref name="typeName" /> in <paramref name="namespaceName" />, keyed by
    ///     field name.
    /// </summary>
    /// <remarks>
    ///     Parameterised because two generators use this arrangement — the CQRS contracts and the
    ///     external components' prop types — and the PE-metadata read is the same walk either way.
    ///     Duplicating it would leave two copies of the #650 reasoning to keep in step.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Read(
        string assemblyPath, string namespaceName, string typeName)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        using (var stream = File.OpenRead(assemblyPath))
        using (var pe = new PEReader(stream))
        {
            if (!pe.HasMetadata)
            {
                return found;
            }

            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                if (!reader.StringComparer.Equals(type.Name, typeName) ||
                    !reader.StringComparer.Equals(type.Namespace, namespaceName))
                {
                    continue;
                }

                foreach (var fieldHandle in type.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    var constantHandle = field.GetDefaultValue();
                    if (constantHandle.IsNil)
                    {
                        continue;
                    }

                    var constant = reader.GetConstant(constantHandle);
                    if (constant.TypeCode != ConstantTypeCode.String)
                    {
                        continue;
                    }

                    // A string constant is stored as raw little-endian UTF-16, with no length prefix —
                    // the blob's own length is the length. Decoded explicitly rather than through
                    // BlobReader.ReadUTF16, whose argument is documented as a character count and
                    // behaves as a byte count: passing RemainingBytes / 2 silently returns the first
                    // half of the string, and half a TypeScript file still parses far enough to look
                    // plausible.
                    var blob = reader.GetBlobReader(constant.Value);
                    found[reader.GetString(field.Name)] =
                        Encoding.Unicode.GetString(blob.ReadBytes(blob.RemainingBytes));
                }

                break;
            }
        }

        return found;
    }

    /// <summary>
    ///     Writes <paramref name="content" /> to <paramref name="path" /> only if it would change.
    /// </summary>
    /// <remarks>
    ///     Load-bearing rather than an optimisation. These files sit inside the front end's source
    ///     tree, so the bundler's watcher is looking at them: rewriting an identical file on every
    ///     build restarts the dev server's dependency graph and, with the MSBuild up-to-date check
    ///     keyed on the same files, can loop a watch build against itself.
    /// </remarks>
    /// <returns><see langword="true" /> when the file was written.</returns>
    public static bool WriteIfDifferent(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
        return true;
    }
}
