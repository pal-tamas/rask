using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Blazor;

/// <summary>
///     One hosted <c>[Parameter]</c>, as both a chain step and a dictionary entry.
/// </summary>
/// <param name="Parameter">The hosted component's own parameter name — the dictionary key.</param>
/// <param name="Name">What the island calls it, which is the chain step's name.</param>
/// <param name="ChainTypeFqn">The generated property's type. Always nullable, so it can be omitted.</param>
/// <param name="EventArg">For an <c>EventCallback&lt;T&gt;</c>, the fully-qualified <c>T</c>.</param>
/// <param name="IsEventCallback">Whether the hosted parameter is an <c>EventCallback</c>.</param>
internal readonly record struct BlazorParam(
    string Parameter,
    string Name,
    string ChainTypeFqn,
    string? EventArg,
    bool IsEventCallback);

/// <summary>
///     Reads the chain steps an island gets from the Blazor component it hosts.
/// </summary>
/// <remarks>
///     <para>
///         Shared deliberately, and it is the whole reason this type exists. Two generators need this
///         list and neither can see the other's output: <c>BlazorGenerator</c> emits the property
///         declarations and the parameter writer, while <c>ComponentFactoryGenerator</c> emits the
///         chain setters for those same properties. Computed twice, the two would drift the first time
///         either changed a rule — and the failure would be a setter for a property that does not
///         exist, or a property no chain can reach.
///     </para>
/// </remarks>
internal static class BlazorParameters
{
    /// <summary>The unbound <c>BlazorComponent&lt;T&gt;</c>, by metadata name.</summary>
    public const string IslandBaseMetadataName = "Rask.Blazor.BlazorComponent`1";

    private const string ParameterAttrName = "Microsoft.AspNetCore.Components.ParameterAttribute";
    private const string RenameAttrName = "Rask.Blazor.BlazorParameterAttribute";
    private const string EventCallbackName = "Microsoft.AspNetCore.Components.EventCallback";
    private const string RenderFragmentName = "Microsoft.AspNetCore.Components.RenderFragment";

    /// <summary>
    ///     Fully qualified, and carrying the <c>?</c> on a nullable reference type.
    /// </summary>
    /// <remarks>
    ///     The plain <c>FullyQualifiedFormat</c> omits nullable-reference modifiers, so <c>string?</c>
    ///     comes back as <c>global::System.String</c> — which then reads as a non-nullable property
    ///     with no initializer, i.e. a REQUIRED chain step (RASK001), and every call site is forced to
    ///     supply it. The same format the factory generator uses when it needs the real shape.
    /// </remarks>
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>The component <paramref name="type" /> hosts, or null when it hosts none.</summary>
    public static INamedTypeSymbol? HostedTypeOf(INamedTypeSymbol type, Compilation compilation)
    {
        var unbound = compilation.GetTypeByMetadataName(IslandBaseMetadataName);
        if (unbound is null)
        {
            return null;
        }

        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            // The base is the CONSTRUCTED BlazorComponent<MudChart>, so a direct comparison against
            // the unbound symbol never matches — it has to go through OriginalDefinition.
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, unbound)
                && t.TypeArguments.Length == 1
                && t.TypeArguments[0] is INamedTypeSymbol hosted)
            {
                return hosted;
            }
        }

        return null;
    }

    /// <summary>
    ///     The parameters <paramref name="island" /> should expose for the component it hosts.
    /// </summary>
    /// <remarks>
    ///     Empty when the hosted type cannot be resolved — a <c>.razor</c> in this same project is
    ///     produced by the Razor source generator, which no other generator can see. That is RASK066;
    ///     the island still renders, with nothing generated for it.
    /// </remarks>
    public static List<BlazorParam> Read(
        INamedTypeSymbol island,
        INamedTypeSymbol hosted,
        Compilation compilation)
    {
        var result = new List<BlazorParam>();
        var parameterAttr = compilation.GetTypeByMetadataName(ParameterAttrName);
        if (parameterAttr is null || hosted.TypeKind == TypeKind.Error)
        {
            return result;
        }

        var renameAttr = compilation.GetTypeByMetadataName(RenameAttrName);

        // Anything the island (or a base) already declares is an explicit override and wins outright.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        for (var t = island; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers())
            {
                declared.Add(m.Name);
            }
        }

        var renames = ReadRenames(island, renameAttr);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (var t = hosted; t is not null; t = t.BaseType)
        {
            foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.IsStatic
                    || prop.SetMethod is null
                    || prop.DeclaredAccessibility != Accessibility.Public
                    || !prop.GetAttributes().Any(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, parameterAttr)))
                {
                    continue;
                }

                // ChildContent reaches the component through the indexer, as Rask children.
                if (prop.Name == "ChildContent" || !taken.Add(prop.Name))
                {
                    continue;
                }

                var typeFqn = prop.Type.ToDisplayString(TypeFormat);

                // A templated parameter has no Rask equivalent yet. Skipping is better than emitting
                // a step that compiles and cannot work.
                if (typeFqn.StartsWith("global::" + RenderFragmentName, StringComparison.Ordinal))
                {
                    continue;
                }

                var name = renames.TryGetValue(prop.Name, out var renamed) ? renamed : prop.Name;
                if (declared.Contains(name))
                {
                    continue;
                }

                var isCallback = typeFqn.StartsWith("global::" + EventCallbackName, StringComparison.Ordinal);
                var eventArg = isCallback && prop.Type is INamedTypeSymbol { TypeArguments.Length: 1 } named
                    ? named.TypeArguments[0].ToDisplayString(TypeFormat)
                    : null;

                result.Add(new BlazorParam(
                    prop.Name,
                    name,
                    ChainType(prop.Type, typeFqn, isCallback, eventArg),
                    eventArg,
                    isCallback));
            }
        }

        return result;
    }

    /// <summary>
    ///     The type the generated property carries.
    /// </summary>
    /// <remarks>
    ///     Always nullable, and that is load-bearing rather than tidy: a non-nullable property with no
    ///     initializer is a REQUIRED chain step (RASK001), which would force every call site to supply
    ///     every parameter the hosted component happens to declare. Nullable also carries the meaning
    ///     the writer needs — null means "not specified", so the key is omitted and the hosted
    ///     component keeps its own default.
    /// </remarks>
    private static string ChainType(ITypeSymbol type, string typeFqn, bool isCallback, string? eventArg)
    {
        if (isCallback)
        {
            // A Blazor EventCallback becomes an ordinary delegate, because that is what a Rask
            // callback prop is — there is no Callback/EventCallback wrapper type in this framework.
            return eventArg is null
                ? "global::System.Action?"
                : $"global::System.Action<{eventArg}>?";
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated
            || typeFqn.EndsWith("?", StringComparison.Ordinal))
        {
            return typeFqn;
        }

        return typeFqn + "?";
    }

    private static Dictionary<string, string> ReadRenames(INamedTypeSymbol island, INamedTypeSymbol? renameAttr)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (renameAttr is null)
        {
            return renames;
        }

        foreach (var member in island.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, renameAttr)
                    && attr.ConstructorArguments.Length == 1
                    && attr.ConstructorArguments[0].Value is string target)
                {
                    renames[target] = member.Name;
                }
            }
        }

        return renames;
    }
}
