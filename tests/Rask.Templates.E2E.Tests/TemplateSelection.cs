namespace Rask.Templates.E2E.Tests;

/// <summary>
///     Narrows every template theory to the templates named in <c>RASK_TEMPLATE_ONLY</c>.
/// </summary>
/// <remarks>
///     <para>
///         A VSTest filter cannot express "one case of this theory" here. A theory's display name
///         carries its argument — <c>…_handler(key: "react")</c> — and the parentheses and quotes in it
///         survive neither route: <c>dotnet test --filter</c> passes the string through MSBuild, which
///         rejects <c>)</c> outright (MSB4177), and <c>dotnet vstest --TestCaseFilter</c> has no escape
///         sequence for them either. Filtering by method name alone runs every template, and on this
///         gate one template is minutes.
///     </para>
///     <para>
///         So the selection is an environment variable, read by the theory data rather than by the
///         runner: <c>RASK_TEMPLATE_ONLY=react</c>, or a comma-separated list. Unset, every template
///         runs — the gate's default is unchanged, and there is no way to leave a narrowing switched on
///         in CI by accident, because the gate scripts never set it.
///     </para>
/// </remarks>
internal static class TemplateSelection
{
    /// <summary>The templates to run, out of <paramref name="all" />.</summary>
    public static IEnumerable<string> Apply(IEnumerable<string> all)
    {
        var only = Environment.GetEnvironmentVariable("RASK_TEMPLATE_ONLY");

        if (string.IsNullOrWhiteSpace(only))
        {
            return all;
        }

        var wanted = only
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // An intersection rather than the names as given: a typo then runs nothing, visibly, instead
        // of scaffolding a template that does not exist and failing somewhere further in.
        return all.Where(wanted.Contains);
    }
}
