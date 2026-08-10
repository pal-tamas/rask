using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Tools.BuilderRewrite;

/// <summary>
///     Makes a type that builds markup a markup HOST, so its call sites become rewritable at all.
/// </summary>
/// <remarks>
///     <para>
///         Entries are inherited members, so they are in scope only inside a component, a type deriving
///         from <c>RaskMarkup</c>, a type carrying <c>[RaskMarkup]</c>, or a type nested in one. A test
///         class and a static markup helper are none of those, which is why roughly a third of the repo's
///         call sites were untouchable by the first pass.
///     </para>
///     <para>
///         Two forms, and only one thing chooses between them: the base slot. <c>: RaskMarkup</c> is the
///         cheap one — the 166 framework entries arrive by inheritance — and it is taken whenever the slot
///         is free. <c>[RaskMarkup]</c> is for where it is not: a <c>static</c> class, or a type whose base
///         belongs to someone else.
///     </para>
///     <para>
///         A name collision does <b>not</b> argue for the attribute, which is the intuitive-but-wrong rule.
///         When an attributed type's base slot is free the generator writes <c>: RaskMarkup</c> into its own
///         partial, so the entry is inherited either way and a member named after a tag (<c>Label</c>,
///         <c>Body</c>, <c>Thead</c>, <c>Html</c>, …) still hides it — CS0108, a build break here. The only
///         cure is <c>new</c> on that member, and both forms owe it equally.
///     </para>
///     <para>
///         The opt-in goes on the OUTERMOST enclosing type, not the one holding the call site: simple-name
///         lookup walks out through enclosing types, so one host covers every type nested in it. And it is
///         always direct — never on a shared base in the hope subclasses inherit it, which was tried and
///         turned fourteen subclasses into hosts that then owed <c>partial</c> in files naming no markup.
///     </para>
/// </remarks>
internal sealed class HostConverter
{
    private readonly SurfaceModel _surface;
    private readonly CSharpCompilation _compilation;
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    public HostConverter(CSharpCompilation compilation, SurfaceModel surface)
    {
        _compilation = compilation;
        _surface = surface;
    }

    public sealed record HostResult(
        SyntaxTree? Rewritten,
        IReadOnlyList<string> Hosts,
        IReadOnlyList<string> Blocked);

    public HostResult Convert(SyntaxTree tree)
    {
        if (tree.GetText().ToString().Contains(FileRewriter.OptOutMarker, StringComparison.Ordinal))
        {
            return new HostResult(null, Array.Empty<string>(), Array.Empty<string>());
        }

        var model = _compilation.GetSemanticModel(tree);

        // Every outermost type in this file that holds a factory call site and is not already a host.
        var candidates = new Dictionary<TypeDeclarationSyntax, INamedTypeSymbol>();
        var blocked = new List<string>();
        foreach (var node in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method || !_surface.IsFactory(method))
            {
                continue;
            }

            var outermost = node.Ancestors().OfType<TypeDeclarationSyntax>().LastOrDefault();
            if (outermost is null || candidates.ContainsKey(outermost))
            {
                continue;
            }

            if (model.GetDeclaredSymbol(outermost) is not { } symbol || _surface.IsInMarkupHost(symbol))
            {
                continue;
            }

            // A type that declares a nested COMPONENT used to be excluded here: the generator injected one
            // entry per reachable component into the host's own partial, named after the component, and
            // `DependencyInjectionTests.GreetingComponent` the entry collided with
            // `DependencyInjectionTests.GreetingComponent` the nested class — CS0102, in generated source,
            // out of a one-line opt-in. The generator now skips a name the host DECLARES as well as one it
            // already reaches, so the exclusion is gone and the 190 types it cost are hosts like any other.
            candidates[outermost] = symbol;
        }

        if (candidates.Count == 0)
        {
            return new HostResult(null, Array.Empty<string>(), blocked);
        }

        var applied = new List<string>();
        var root = tree.GetRoot().ReplaceNodes(
            candidates.Keys,
            (original, current) =>
            {
                var symbol = candidates[original];
                var fqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // A partial type declared in two files must not be given the opt-in twice.
                if (!_done.Add(fqn))
                {
                    return Partial(current);
                }

                // Only the base slot decides. A name collision does NOT argue for the attribute the way it
                // first appears to: when the attributed type's base slot is free the generator writes
                // `: RaskMarkup` into its own partial, so the entry is inherited either way and CS0108
                // comes back regardless. `new` on the colliding member is the only cure, and it is owed by
                // both forms — measured, on BsDataGridColumnsTests, which took the attribute and hid
                // `Thead` anyway.
                var canDeriveFrom = symbol is { IsStatic: false, BaseType.SpecialType: SpecialType.System_Object };
                applied.Add($"{fqn} ({(canDeriveFrom ? ": RaskMarkup" : "[RaskMarkup]")})");
                return canDeriveFrom ? Derive(Partial(current)) : Attribute(Partial(current));
            });

        return new HostResult(tree.WithRootAndOptions(root, tree.Options), applied, blocked);
    }

    private static TypeDeclarationSyntax Partial(TypeDeclarationSyntax declaration) =>
        declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            ? declaration
            : declaration.WithModifiers(declaration.Modifiers.Add(
                Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(Space)));

    private static TypeDeclarationSyntax Derive(TypeDeclarationSyntax declaration)
    {
        // A base class has to come first in the list; anything already there is an interface, because
        // this form is only chosen when the base slot was free. Built by parsing rather than by
        // assembling tokens, so the separators carry their own spacing.
        var types = declaration.BaseList?.Types.Select(t => t.ToString()).ToList() ?? new List<string>();
        types.Insert(0, "global::Rask.Core.RaskMarkup");
        var parsed = ((ClassDeclarationSyntax)ParseMemberDeclaration(
            $"class __X : {string.Join(", ", types)} {{}}")!).BaseList!;

        // Whatever followed the name (or the old base list) — the newline before the brace, usually —
        // has to end up after the new list, or the opening brace joins the declaration.
        var anchor = declaration.BaseList is { } old
            ? old.GetLastToken()
            : (SyntaxToken?)declaration.TypeParameterList?.GetLastToken() ?? declaration.Identifier;
        var tail = anchor.TrailingTrivia;

        return declaration
            .ReplaceToken(anchor, anchor.WithTrailingTrivia())
            .WithBaseList(parsed.WithLeadingTrivia(Space).WithTrailingTrivia(tail));
    }

    private static TypeDeclarationSyntax Attribute(TypeDeclarationSyntax declaration)
    {
        var attribute = AttributeList(SingletonSeparatedList(
            SyntaxFactory.Attribute(ParseName("global::Rask.Core.RaskMarkup"))));

        // The attribute takes the declaration's leading trivia — its indentation and any doc comment
        // stays above it, not stranded between the attribute and the type.
        var leading = declaration.GetLeadingTrivia();
        return declaration
            .WithoutLeadingTrivia()
            .WithAttributeLists(declaration.AttributeLists.Insert(0, attribute.WithLeadingTrivia(leading)
                .WithTrailingTrivia(EndOfLine(Environment.NewLine), Whitespace(IndentOf(leading)))));
    }

    private static string IndentOf(SyntaxTriviaList leading)
    {
        var last = leading.LastOrDefault();
        return last.IsKind(SyntaxKind.WhitespaceTrivia) ? last.ToString() : "";
    }
}
