using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK146 — a bare <c>DateTime</c> in the domain becomes a value object of Rask's: <c>public DateTime FueledAt</c> on
///     <c>Delivery</c> becomes <c>public DeliveryFueledAt FueledAt</c>, with
///     <c>public sealed partial record DeliveryFueledAt(DateTime Value) : Moment(Value);</c> written after the type — or a
///     <c>Day</c> over a <c>DateOnly</c>. Both are offered; the <c>Day</c> first where the name says a day
///     (<c>DueDate</c>, <c>PayDay</c>, <c>DeliveredOn</c>).
/// </summary>
/// <remarks>
///     Conservative on purpose: it changes the declaration and adds the record, and leaves every read and write of the
///     member to the compiler, which then names each one (<c>FueledAt = Clock.Wall</c> becomes
///     <c>FueledAt = DeliveryFueledAt.Create(Clock.Wall)</c> by hand). No fix where a type of that name exists already,
///     for a field declared beside others, or for a value object's own positional member, which its base is handed.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DomainTimeCodeFixProvider))]
[Shared]
public sealed class DomainTimeCodeFixProvider : CodeFixProvider
{
    private const string ReadsAsDay = "ReadsAsDay";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK146"];

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics[0];
        if (root is not CompilationUnitSyntax unit || model is null ||
            Declared(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)) is not { } declared ||
            declared.Owner.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>() is not { } owner ||
            model.GetDeclaredSymbol(owner, context.CancellationToken) is not { } ownerType)
        {
            return;
        }

        var name = ownerType.Name + Capitalized(declared.Name);
        if (model.LookupNamespacesAndTypes(owner.SpanStart, name: name).Length > 0)
        {
            return;
        }

        var moment = Action("Hold it as a Moment (an instant, stored as UTC)", "RASK146_Moment", "DateTime", "Moment");
        var day = Action("Hold it as a Day (a calendar day)", "RASK146_Day", "DateOnly", "Day");
        var dayFirst = diagnostic.Properties.ContainsKey(ReadsAsDay);
        context.RegisterCodeFix(dayFirst ? day : moment, diagnostic);
        context.RegisterCodeFix(dayFirst ? moment : day, diagnostic);

        CodeAction Action(string title, string key, string held, string @base) =>
            CodeAction.Create(title, _ => Task.FromResult(Fixed(context.Document, unit, declared, owner, name, held, @base)), key);
    }

    private static Document Fixed(
        Document document, CompilationUnitSyntax unit, (SyntaxNode Owner, TypeSyntax Type, string Name) declared,
        BaseTypeDeclarationSyntax owner, string name, string held, string @base)
    {
        var nullable = declared.Type is NullableTypeSyntax;
        TypeSyntax replacement = IdentifierName(name);
        if (nullable)
        {
            replacement = NullableType(replacement);
        }

        var outermost = owner.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Last();
        var record = (RecordDeclarationSyntax)ParseMemberDeclaration(
            $"public sealed partial record {name}({held} Value) : {@base}(Value);")!;

        var tracked = unit.TrackNodes(declared.Type, outermost);
        tracked = tracked.ReplaceNode(tracked.GetCurrentNode(declared.Type)!, replacement.WithTriviaFrom(declared.Type));
        var after = tracked.GetCurrentNode(outermost)!;
        tracked = tracked.InsertNodesAfter(after, [record.WithLeadingTrivia(ElasticCarriageReturnLineFeed).WithAdditionalAnnotations(Formatter.Annotation)]);
        return document.WithSyntaxRoot(WithValues(tracked));
    }

    // The member the diagnostic is on: its declaration, the type written for it, and its name.
    private static (SyntaxNode Owner, TypeSyntax Type, string Name)? Declared(SyntaxNode node)
    {
        switch (node.FirstAncestorOrSelf<SyntaxNode>(static n => n is PropertyDeclarationSyntax or ParameterSyntax or VariableDeclaratorSyntax))
        {
            case PropertyDeclarationSyntax property:
                return (property, property.Type, property.Identifier.ValueText);
            case ParameterSyntax { Type: { } type, Parent.Parent: RecordDeclarationSyntax record } parameter
                when !IsValueObject(record):
                return (parameter, type, parameter.Identifier.ValueText);
            case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: 1 } declaration } variable:
                return (variable, declaration.Type, variable.Identifier.ValueText);
            default:
                return null;
        }
    }

    // A record declared from one of Rask's value bases hands its positional members to the base: changing one's type
    // would change what the base is given.
    private static bool IsValueObject(RecordDeclarationSyntax record) =>
        record.BaseList?.Types.FirstOrDefault() is PrimaryConstructorBaseTypeSyntax;

    private static string Capitalized(string name)
    {
        var trimmed = name.TrimStart('_');
        return trimmed.Length == 0 ? name : char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }

    // `using Rask.Values;`, where the file does not have it yet.
    private static CompilationUnitSyntax WithValues(CompilationUnitSyntax unit) =>
        unit.Usings.Any(static u => string.Equals(u.Name?.ToString(), "Rask.Values", System.StringComparison.Ordinal))
            ? unit
            : unit.AddUsings(UsingDirective(ParseName("Rask.Values")).WithTrailingTrivia(ElasticCarriageReturnLineFeed));
}
