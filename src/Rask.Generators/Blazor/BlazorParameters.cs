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
/// <param name="ChainTypeFqn">The generated property's type.</param>
/// <param name="EventArg">For an <c>EventCallback&lt;T&gt;</c>, the fully-qualified <c>T</c>.</param>
/// <param name="IsEventCallback">Whether the hosted parameter is an <c>EventCallback</c>.</param>
/// <param name="IsRequired">
///     Whether the hosted component marked it <c>[EditorRequired]</c>, which makes it a required
///     chain step rather than an optional one.
/// </param>
internal readonly record struct BlazorParam(
    string Parameter,
    string Name,
    string ChainTypeFqn,
    string? EventArg,
    bool IsEventCallback,
    bool IsRequired);

/// <summary>
///     Reads the chain steps an island gets from the Blazor component it hosts.
/// </summary>
/// <remarks>
///     <para>
///         Shared deliberately, and it is the whole reason this type exists. Three places need this
///         list and no source generator can see another's output: <c>BlazorGenerator</c> emits the
///         property declarations and the parameter writer, <c>ComponentFactoryGenerator</c> emits
///         their chain setters, and the same generator needs the NAMES earlier still, to stop an
///         injected markup entry colliding with a property that does not exist yet. Computed
///         separately, they would diverge the first time any rule changed.
///     </para>
///     <para>
///         Everything here is answered from symbols alone, with no <c>Compilation</c>: the names are
///         needed while building a candidate, long before one is in hand. Attributes are therefore
///         matched by their full display name rather than by symbol identity.
///     </para>
/// </remarks>
internal static class BlazorParameters
{
    private const string IslandBaseNamespace = "Rask.Blazor";
    private const string IslandBaseName = "BlazorComponent";
    private const string ParameterAttrName = "Microsoft.AspNetCore.Components.ParameterAttribute";
    private const string EditorRequiredAttrName = "Microsoft.AspNetCore.Components.EditorRequiredAttribute";
    private const string RenameAttrName = "Rask.Blazor.BlazorParameterAttribute";
    private const string EventCallbackName = "Microsoft.AspNetCore.Components.EventCallback";
    private const string RenderFragmentName = "Microsoft.AspNetCore.Components.RenderFragment";

    /// <summary>
    ///     Fully qualified, and carrying the <c>?</c> on a nullable reference type.
    /// </summary>
    /// <remarks>
    ///     The plain <c>FullyQualifiedFormat</c> omits nullable-reference modifiers, so <c>string?</c>
    ///     comes back as <c>string</c> — which then reads as a non-nullable property with no
    ///     initializer, i.e. a REQUIRED chain step, and every call site is forced to supply it.
    /// </remarks>
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>The component <paramref name="type" /> hosts, or null when it hosts none.</summary>
    public static INamedTypeSymbol? HostedTypeOf(INamedTypeSymbol type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            // The base is the CONSTRUCTED BlazorComponent<MudChart>, so its type argument is what is
            // wanted; matched on shape rather than symbol identity because this runs where no
            // Compilation is available to resolve the unbound definition.
            var def = t.OriginalDefinition;
            if (def.Name == IslandBaseName
                && def.Arity == 1
                && def.ContainingNamespace?.ToDisplayString() == IslandBaseNamespace
                && t.TypeArguments.Length == 1
                && t.TypeArguments[0] is INamedTypeSymbol hosted)
            {
                return hosted;
            }
        }

        return null;
    }

    /// <summary>The chain-step names <paramref name="island" /> will gain, if it is an island.</summary>
    /// <remarks>
    ///     Needed before the properties exist. A markup host has an entry injected per component name
    ///     (<c>Label</c>, <c>Title</c>, <c>Form</c>, …), and a hosted parameter with one of those names
    ///     would land in the same class as the injected entry — CS0102, a duplicate member. Feeding
    ///     these names into the host's known members makes the injection skip them, using the
    ///     collision check that is already there.
    /// </remarks>
    public static IEnumerable<string> StepNames(INamedTypeSymbol island) =>
        HostedTypeOf(island) is { } hosted
            ? Read(island, hosted).Select(static p => p.Name)
            : [];

    /// <summary>
    ///     The parameters <paramref name="island" /> should expose for the component it hosts.
    /// </summary>
    /// <remarks>
    ///     Empty when the hosted type cannot be resolved — a <c>.razor</c> in this same project is
    ///     produced by the Razor source generator, which no other generator can see. That is RASK066;
    ///     the island still renders, with nothing generated for it.
    /// </remarks>
    public static List<BlazorParam> Read(INamedTypeSymbol island, INamedTypeSymbol hosted)
    {
        var result = new List<BlazorParam>();
        if (hosted.TypeKind == TypeKind.Error)
        {
            return result;
        }

        // Anything the island (or a base) already declares is an explicit override and wins outright.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        for (var t = island; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers())
            {
                declared.Add(m.Name);
            }
        }

        var renames = ReadRenames(island);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (var t = hosted; t is not null; t = t.BaseType)
        {
            foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.IsStatic
                    || prop.SetMethod is null
                    || prop.DeclaredAccessibility != Accessibility.Public
                    || !HasAttribute(prop, ParameterAttrName))
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

                // [EditorRequired] is Blazor's own way of saying a parameter is mandatory, so it maps
                // onto Rask's own: a required chain step the call site cannot omit. Each framework
                // states it in its own idiom and neither has to learn the other's.
                //
                // Never a callback, even when marked: "you must handle this event" is not something
                // Rask's chain can usefully insist on, and an unwired callback is simply not wired —
                // exactly as for any other Rask component.
                var isRequired = !isCallback && HasAttribute(prop, EditorRequiredAttrName);

                result.Add(new BlazorParam(
                    prop.Name,
                    name,
                    ChainType(prop.Type, typeFqn, isCallback, eventArg, isRequired),
                    eventArg,
                    isCallback,
                    isRequired));
            }
        }

        return result;
    }

    /// <summary>The type the generated property carries.</summary>
    /// <remarks>
    ///     Nullable unless the hosted component marked it required, and that is load-bearing rather
    ///     than tidy: a non-nullable property with no initializer is a REQUIRED chain step, which for
    ///     an unmarked parameter would force every call site to supply everything the component
    ///     happens to declare. Nullable also carries the meaning the writer needs — null means "not
    ///     specified", so the key is omitted and the component keeps its own default.
    /// </remarks>
    private static string ChainType(
        ITypeSymbol type,
        string typeFqn,
        bool isCallback,
        string? eventArg,
        bool isRequired)
    {
        if (isCallback)
        {
            // A Blazor EventCallback becomes an ordinary delegate, because that is what a Rask
            // callback prop is — there is no Callback/EventCallback wrapper type in this framework.
            return eventArg is null
                ? "global::System.Action?"
                : $"global::System.Action<{eventArg}>?";
        }

        if (isRequired)
        {
            return typeFqn.TrimEnd('?');
        }

        return type.NullableAnnotation == NullableAnnotation.Annotated
               || typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? typeFqn
            : typeFqn + "?";
    }

    private static bool HasAttribute(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fullName);

    private static Dictionary<string, string> ReadRenames(INamedTypeSymbol island)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in island.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == RenameAttrName
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
