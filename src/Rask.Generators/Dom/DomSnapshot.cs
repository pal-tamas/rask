using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Json;

namespace Rask.Generators.Dom;

/// <summary>
///     The element surface MDN describes, read from <c>src/Rask.Core/Dom/mdn.snapshot.json</c> (written by
///     <c>scripts/mdn/refresh.sh</c>). Only Rask.Core passes it in; every other compilation sees none.
/// </summary>
internal static class DomSnapshot
{
    public const string FileName = "mdn.snapshot.json";

    /// <summary>The HTML void elements: the tags that never take children or a closing tag.</summary>
    public static IncrementalValueProvider<EquatableArray<string>> VoidTags(IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Where(static text => string.Equals(Path.GetFileName(text.Path), FileName, StringComparison.OrdinalIgnoreCase))
            .Select(static (text, ct) => ReadVoidTags(text.GetText(ct)?.ToString() ?? string.Empty))
            .Collect()
            .Select(static (all, _) => all.IsDefaultOrEmpty ? default : all[0]);

    private static EquatableArray<string> ReadVoidTags(string json)
    {
        var elements = JsonLite.Parse(json).Root?["elements"];
        if (elements is null)
        {
            return default;
        }

        return new EquatableArray<string>(elements.Items
            .Where(static e => e["void"]?.AsBoolean() == true)
            .Select(static e => e["tag"]?.AsString())
            .OfType<string>());
    }
}
