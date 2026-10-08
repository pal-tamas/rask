using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Site.Tests.Pages;

/// <summary>
/// Holds every <c>Ui.…</c> chain in a piece of C# to the kit as it is built: the entry is a component the kit
/// has, each step is one of that component's props or a value of one, and a kit enum passed to a step is the
/// enum that step takes.
/// </summary>
/// <remarks>
/// Reflection over <c>Rask.Ui</c>, not a compile. The receiver of a chain is the component from the first
/// step to the last, so a step can be checked against one type without binding anything else in the snippet —
/// which is what lets it run over prose that mentions types it never defines.
/// </remarks>
internal static class KitChainChecker
{
    private static readonly Assembly Kit = typeof(Ui).Assembly;

    // Steps the generator writes that no prop is named after, and the prop each one needs: `Of<T>()` states a
    // generic component's type argument, and `Values` is how a control that holds a collection opens on `Value`.
    private static readonly Dictionary<string, string?> ChainSteps = new(StringComparer.Ordinal) { ["Of"] = null, ["Values"] = "Value" };

    /// <summary>What is wrong with the kit chains in <paramref name="code" />, one line each. Empty when nothing is.</summary>
    internal static IEnumerable<string> Problems(string code)
    {
        var root = Parse(code);
        var entries = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(access => access.Expression is IdentifierNameSyntax { Identifier.ValueText: "Ui" });

        return entries.SelectMany(Problems);
    }

    private static IEnumerable<string> Problems(MemberAccessExpressionSyntax entry)
    {
        var name = entry.Name.Identifier.ValueText;
        if (KitEnum(name) is { } values)
        {
            return EnumProblems(entry, values);
        }

        return KitComponent(name) is { } component
            ? StepProblems(entry, component)
            : [$"Ui.{name} is not in the kit"];
    }

    private static IEnumerable<string> EnumProblems(MemberAccessExpressionSyntax entry, Type values)
    {
        if (entry.Parent is MemberAccessExpressionSyntax member && member.Expression == entry
            && !Enum.IsDefined(values, member.Name.Identifier.ValueText))
        {
            yield return $"Ui.{values.Name} has no value '{member.Name.Identifier.ValueText}'";
        }
    }

    private static IEnumerable<string> StepProblems(MemberAccessExpressionSyntax entry, Type component)
    {
        var props = component.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
        ExpressionSyntax receiver = entry;
        while (receiver.Parent is MemberAccessExpressionSyntax step && step.Expression == receiver)
        {
            var call = step.Parent as InvocationExpressionSyntax;
            if (StepProblem(entry, props, step.Name.Identifier.ValueText, call) is { } problem)
            {
                yield return problem;
            }

            receiver = call is not null && call.Expression == step ? call : step;
        }
    }

    private static string? StepProblem(
        MemberAccessExpressionSyntax entry, PropertyInfo[] props, string step, InvocationExpressionSyntax? call)
    {
        var prop = Array.Find(props, p => p.Name == step);
        if (prop is null)
        {
            return IsChainStep(props, step) || props.Any(p => EnumOf(p) is { } values && Enum.IsDefined(values, step))
                ? null
                : $"{entry} has no step '{step}'";
        }

        // `.Size(Ui.Size.Xl)` on a component whose Size is its own enum: the name is right and the call is not.
        return EnumOf(prop) is { } taken && PassedKitEnum(call) is { } passed && passed != taken
            ? $"{entry}.{step} takes {Display(taken)}, not {Display(passed)}"
            : null;
    }

    private static bool IsChainStep(PropertyInfo[] props, string step) =>
        ChainSteps.TryGetValue(step, out var needs) && (needs is null || Array.Exists(props, p => p.Name == needs));

    private static Type? PassedKitEnum(InvocationExpressionSyntax? call) =>
        call?.ArgumentList.Arguments is [{ Expression: MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax of } }]
        && of.Expression is IdentifierNameSyntax { Identifier.ValueText: "Ui" }
            ? KitEnum(of.Name.Identifier.ValueText)
            : null;

    private static Type? EnumOf(PropertyInfo prop)
    {
        var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        return type.IsEnum ? type : null;
    }

    private static Type? KitEnum(string name) =>
        typeof(Ui).GetNestedType(name, BindingFlags.Public) is { IsEnum: true } values ? values : null;

    // `Ui.Input` is UiInput<T>: a generic component keeps its entry's name, so the arity is not part of the match.
    private static Type? KitComponent(string name) =>
        Array.Find(Kit.GetExportedTypes(), t => t.Name == "Ui" + name || t.Name.StartsWith("Ui" + name + "`", StringComparison.Ordinal));

    private static string Display(Type values) => values.DeclaringType == typeof(Ui) ? "Ui." + values.Name : values.Name;

    // A snippet is a file, a member, statements or initializer entries, and says nowhere which. The first shape
    // that parses clean is the one it is; a sketch that elides code parses as none, and is read as statements.
    private static SyntaxNode Parse(string code)
    {
        string[] shapes = [code, $"class Wrap {{ {code} }}", $"class Wrap {{ void M() {{ {code} }} }}"];
        var trees = shapes.Select(shape => CSharpSyntaxTree.ParseText(shape, new CSharpParseOptions(LanguageVersion.Latest))).ToArray();
        var clean = Array.Find(trees, tree => !tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error));

        return (clean ?? trees[^1]).GetRoot();
    }
}
