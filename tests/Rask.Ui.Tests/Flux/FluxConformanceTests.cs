using System.Reflection;
using System.Text.Json;

namespace Rask.UiTests.Flux;

/// <summary>
///     Every Rask.Ui component that mirrors a Flux UI component takes what Flux documents for it.
/// </summary>
/// <remarks>
///     <para>
///     <c>flux.snapshot.json</c> is Flux's reference — every component, its props, the values each accepts —
///     read from fluxui.dev's docs by <c>scripts/flux/refresh.mjs</c>. This holds each built component to it: a
///     documented prop with no property of that name, or a documented value with no enum member, fails here.
///     So does a refresh that brings a prop Flux added since.
///     </para>
///     <para>
///     What Flux says with a Livewire or Alpine attribute (<c>wire:model</c>, <c>x-on:click</c>) is said in
///     Rask with <c>Bind</c>/<c>Value</c> and a <c>Callback</c>, and is skipped by rule. Anything else that
///     does not translate is named in <see cref="NotTranslated" /> with the reason, one line each.
///     </para>
/// </remarks>
public sealed class FluxConformanceTests
{
    /// <summary>Flux part → the Rask.Ui type that mirrors it. A component joins this when it is built.</summary>
    private static readonly Dictionary<string, Type> Built = new(StringComparer.Ordinal)
    {
        ["flux:separator"] = typeof(UiSeparator),
    };

    /// <summary><c>part/prop</c> or <c>part/prop=value</c> → why Rask.Ui does not carry it.</summary>
    private static readonly Dictionary<string, string> NotTranslated = new(StringComparer.Ordinal);

    private static readonly BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    [Fact]
    public void The_snapshot_holds_the_whole_catalogue()
    {
        var parts = Parts().Select(part => part.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(53, Snapshot.RootElement.GetProperty("pages").GetArrayLength());
        Assert.All(Built.Keys, part => Assert.Contains(part, parts));
    }

    [Fact]
    public void Every_built_component_takes_what_Flux_documents()
    {
        var missing = new List<string>();

        foreach (var (part, props) in Parts().Where(part => Built.ContainsKey(part.Name)))
        {
            foreach (var prop in props.Where(prop => !IsDirective(prop.Name) && !NotTranslated.ContainsKey($"{part}/{prop.Name}")))
            {
                var property = Built[part].GetProperty(Pascal(prop.Name), Public | BindingFlags.IgnoreCase);
                if (property is null)
                {
                    missing.Add($"{part}/{prop.Name}: no {Built[part].Name}.{Pascal(prop.Name)}");
                    continue;
                }

                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (!type.IsEnum)
                {
                    continue;
                }

                missing.AddRange(prop.Options
                    .Where(option => !NotTranslated.ContainsKey($"{part}/{prop.Name}={option}"))
                    .Where(option => !Enum.GetNames(type).Contains(Pascal(option), StringComparer.OrdinalIgnoreCase))
                    .Select(option => $"{part}/{prop.Name}={option}: no {type.Name}.{Pascal(option)}"));
            }
        }

        Assert.True(missing.Count == 0, "Flux documents these and Rask.Ui does not take them:\n  " + string.Join("\n  ", missing));
    }

    private static JsonDocument Snapshot { get; } = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoRoot.FullPath, "tests", "Rask.Ui.Tests", "Flux", "flux.snapshot.json")));

    private static IEnumerable<(string Name, List<(string Name, string[] Options)> Props)> Parts() =>
        from page in Snapshot.RootElement.GetProperty("pages").EnumerateArray()
        from part in page.GetProperty("parts").EnumerateArray()
        select (
            part.GetProperty("name").GetString()!,
            part.TryGetProperty("props", out var props)
                ? props.EnumerateArray().Select(prop => (
                    prop.GetProperty("name").GetString()!,
                    prop.TryGetProperty("options", out var options)
                        ? options.EnumerateArray().Select(option => option.GetString()!).ToArray()
                        : [])).ToList()
                : []);

    // wire:model, x-model, :accent — a framework directive rather than a prop of the component.
    private static bool IsDirective(string prop) =>
        prop.StartsWith("wire:", StringComparison.Ordinal) || prop.StartsWith("x-", StringComparison.Ordinal);

    // icon:trailing -> IconTrailing, tooltip:position -> TooltipPosition, 2xl -> 2xl is left to the enum's own spelling.
    private static string Pascal(string name) =>
        string.Concat(name.Split('-', ':', '.', ' ').Where(word => word.Length > 0).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
