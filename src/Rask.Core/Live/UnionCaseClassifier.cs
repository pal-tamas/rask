using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Core.Live;

/// <summary>
///     Reads a C# union back as the case its JSON object's properties name, on every JSON surface a union crosses in
///     Rask: an interop call's arguments and result, and the persisted-state bag the resume record carries.
/// </summary>
/// <remarks>
///     <para>
///         System.Text.Json writes a union as its case value, bare, and on its own reads it back by the JSON value's
///         kind alone — so <c>union Shape(Circle, Square)</c>, two objects, writes and then cannot be read. .NET's
///         <see cref="JsonUnionTypeStructuralClassifier" /> settles it by which properties are present, which is what
///         a union of records needs, and it needs no attribute on the union.
///     </para>
///     <para>
///         Installed for every union, it would also <em>refuse</em> one it cannot tell apart (a case whose properties
///         are all another's), and at write time too — a union that crosses one way today would throw. So a union
///         the structural classifier cannot settle is handed back to System.Text.Json's own default instead: it writes
///         as it always did, and a read of an ambiguous value fails as it always did. A union that names its own
///         classifier (<see cref="JsonUnionAttribute.TypeClassifier" />) keeps it.
///     </para>
/// </remarks>
internal sealed class UnionCaseClassifier : JsonTypeClassifierFactory
{
    private static readonly JsonUnionTypeStructuralClassifier Structural = new();

    internal static UnionCaseClassifier Instance { get; } = new();

    private UnionCaseClassifier()
    {
    }

    public override bool CanClassify(JsonTypeClassifierContext context) =>
        context.Kind == JsonTypeClassifierKind.Union && Structural.CanClassify(context);

    // System.Text.Json stores what this returns as the union's JsonTypeInfo.TypeClassifier, where null — that property's
    // default — is its own classification by value kind. That is the fallback, though the override is annotated non-null.
    public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options)
    {
        try
        {
            return Structural.CreateJsonClassifier(context, options);
        }
        catch (NotSupportedException)
        {
            return null!;
        }
    }
}
