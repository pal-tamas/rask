using System;
using System.Collections;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    /// <summary>
    ///     A property's <c>&lt;summary&gt;</c>, flattened to one line, or empty when it has none.
    /// </summary>
    /// <remarks>
    ///     What a chain shows in a tooltip is the setter, not the property — so unless the summary is
    ///     carried across, hovering <c>.Placeholder(…)</c> says nothing at all while the property it
    ///     writes is fully documented.
    ///     <para>
    ///         <c>&lt;inheritdoc/&gt;</c> is followed by hand. It does NOT arrive resolved:
    ///         <c>GetDocumentationCommentXml</c> hands back the literal <c>&lt;inheritdoc/&gt;</c> element,
    ///         because resolving it is an IDE/DocFX-layer job, not a compiler one. That made every async
    ///         twin in the framework — <c>OnValidSubmitAsync</c>, <c>ValidateAsync</c>, each written as
    ///         <c>&lt;inheritdoc cref="OnSubmit"/&gt;</c> — emit a setter with no documentation at all,
    ///         while its sibling was fully documented and the source looked complete either way.
    ///     </para>
    /// </remarks>
    private static string SummaryOf(ISymbol symbol) => SummaryOf(symbol, depth: 0);

    private static string SummaryOf(ISymbol symbol, int depth)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml))
        {
            // No doc comment at all. An override or an interface implementation still HAS documentation as
            // far as a reader is concerned — every IDE shows the base member's — and the overwhelmingly
            // common way to write one is to add no comment rather than an explicit <inheritdoc/>. Every
            // form control lands here: Input/Select/Textarea implement IFormControl<T>.Validate, OnChange
            // and AfterBind without redeclaring their docs, so without this the interface can be documented
            // exhaustively and every control's chain still shows nothing.
            return depth < 4 && InheritedMember(symbol) is { } from
                ? SummaryOf(from, depth + 1)
                : string.Empty;
        }

        var open = xml!.IndexOf("<summary>", StringComparison.Ordinal);
        var close = xml.IndexOf("</summary>", StringComparison.Ordinal);
        if (open < 0 || close <= open)
        {
            // No summary of its own — an <inheritdoc/> stands in for one. Depth-capped because a pair of
            // members can point <inheritdoc/> at each other, and this walk has no other terminator.
            if (depth >= 4 || xml.IndexOf("<inheritdoc", StringComparison.Ordinal) < 0)
            {
                return string.Empty;
            }

            return InheritDocTarget(symbol, xml) is { } inherited
                ? SummaryOf(inherited, depth + 1)
                : string.Empty;
        }

        var text = xml.Substring(open + "<summary>".Length, close - open - "<summary>".Length);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }

    /// <summary>
    ///     The member an <c>&lt;inheritdoc/&gt;</c> borrows its summary from, or <see langword="null" />.
    /// </summary>
    /// <remarks>
    ///     Two shapes, which is all C# offers. With a <c>cref</c>, the attribute names the member — Roslyn
    ///     writes it as a documentation-comment id (<c>P:Some.Type.Member</c>), and the member name is what
    ///     follows the last dot. Without one, the doc is inherited the way the language inherits it: from
    ///     the overridden member, else from the interface member this one implements.
    ///     <para>
    ///         A cref is resolved against the CONTAINING TYPE only. Doing it properly needs the
    ///         <c>Compilation</c> (<c>DocumentationCommentId.GetFirstSymbolForDeclarationId</c>), which this
    ///         call site does not have; every use in the framework points at a sibling, and a cross-type
    ///         cref degrades to no summary rather than to a wrong one.
    ///     </para>
    /// </remarks>
    private static ISymbol? InheritDocTarget(ISymbol symbol, string xml)
    {
        var cref = AttributeValue(xml, "cref");
        if (cref is { Length: > 0 })
        {
            var name = cref.Substring(cref.LastIndexOf('.') + 1);
            // A generic type's id carries an arity suffix on the TYPE, not the member, so the member name
            // needs no stripping — but a malformed id would, and an empty name matches nothing anyway.
            return symbol.ContainingType?.GetMembers(name).FirstOrDefault();
        }

        return InheritedMember(symbol);
    }

    /// <summary>
    ///     The member C# itself would inherit documentation from: the one this overrides, else the
    ///     interface member this implements. <see langword="null" /> when it inherits from neither.
    /// </summary>
    private static ISymbol? InheritedMember(ISymbol symbol)
    {
        if (symbol is IPropertySymbol { OverriddenProperty: { } baseProperty })
        {
            return baseProperty;
        }

        if (symbol is IMethodSymbol { OverriddenMethod: { } baseMethod })
        {
            return baseMethod;
        }

        var type = symbol.ContainingType;
        if (type is null)
        {
            return null;
        }

        // Match on the implementation rather than on the name alone: a control can declare a member that
        // merely SHARES a name with an interface member it does not implement, and borrowing docs from an
        // unrelated member is worse than having none.
        return type
            .AllInterfaces
            .SelectMany(i => i.GetMembers(symbol.Name))
            .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(
                type.FindImplementationForInterfaceMember(m), symbol));
    }

    // The value of one attribute on the first <inheritdoc …> element. A hand-rolled read rather than an
    // XML parse: the surrounding string is compiler-produced doc XML, and the generator runs per property
    // on every keystroke in the IDE.
    private static string? AttributeValue(string xml, string attribute)
    {
        var element = xml.IndexOf("<inheritdoc", StringComparison.Ordinal);
        if (element < 0)
        {
            return null;
        }

        var end = xml.IndexOf('>', element);
        var at = xml.IndexOf(attribute + "=\"", element, StringComparison.Ordinal);
        if (at < 0 || (end >= 0 && at > end))
        {
            return null;
        }

        var start = at + attribute.Length + 2;
        var quote = xml.IndexOf('"', start);
        return quote < 0 ? null : xml.Substring(start, quote - start);
    }

    // DataType.Password. The enum member's VALUE is what reaches metadata, so that is what an attribute argument
    // compares against; naming the member here is for the reader.
    private const int PasswordDataType = 11;

    // Words that say "secret" wherever they appear in a name: Password, ApiToken, ClientSecret, ApiKeyHeader.
    private static readonly string[] SensitiveWords =
        ["password", "passcode", "secret", "token", "apikey", "credential"];

    // …and the short ones, matched whole. As substrings they would redact Pinned, Spinner and Session — hiding an
    // ordinary value is its own kind of wrong, and a developer reading their own tree would not know why.
    private static readonly string[] SensitiveNames = ["pin", "ssn"];

    // Whether the devtools must never show a property's value. Decided from what the code says about it: the name
    // developers already use for a secret, or an attribute that declares one — DataAnnotations' password field,
    // WinForms' PasswordPropertyText (which an app may carry for a designer), and Identity's personal-data pair,
    // whose whole purpose is to mark what must not be handed around.
    //
    // Deliberately a blunt rule, and deliberately at BUILD time. A value redacted later has already been read and
    // has usually already crossed a wire; a description that was never written cannot leak. The cost of being
    // wrong is a value a developer must read from their own code, which is where they were anyway.
    private static bool IsSensitiveProp(IPropertySymbol prop)
    {
        foreach (var attribute in prop.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is "PasswordPropertyTextAttribute" or "PersonalDataAttribute" or "ProtectedPersonalDataAttribute")
            {
                return true;
            }

            // [DataType(DataType.Password)] — the enum member's value, not its name, survives to metadata.
            // Written without a slice pattern: this generator targets netstandard2.0, which has no System.Index.
            if (string.Equals(name, "DataTypeAttribute", StringComparison.Ordinal)
                && attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is int dataType
                && dataType == PasswordDataType)
            {
                return true;
            }
        }

        return SensitiveWords.Any(word => prop.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
               || SensitiveNames.Any(name => string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
