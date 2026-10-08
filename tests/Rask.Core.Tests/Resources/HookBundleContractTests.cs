using System.Text.RegularExpressions;

namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract: the behaviour hooks stay OUT of the runtimes every page loads, and the loader that
///     fetches them knows every attribute a hook answers to.
/// </summary>
/// <remarks>
///     <para>
///         The hooks are a bundle of their own (<c>rask-hooks.ts</c>), loaded when a page first carries one of
///         the attributes listed in <c>rask-hook-loader.ts</c>. Two things can silently undo that, and neither
///         fails a build: a runtime module importing a hook module puts it back in every page's download, and a
///         hook that answers to an attribute the loader has never heard of is a hook that never loads on a page
///         carrying only that attribute.
///     </para>
///     <para>
///         Structural, like its neighbours here: the entry points boot a transport against a live document. The
///         behavioural proof — no request without a hook, one with — is <c>RuntimeHookLoadingTests</c> in
///         <c>Rask.Server.E2E.Tests</c>.
///     </para>
/// </remarks>
public partial class HookBundleContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();
    private static readonly string _resources = Path.Combine(_repoRoot, "src", "Rask.Core", "Resources");

    // Named by a hook, and not a reason to load one: read on or inside an element that carries a listed
    // attribute, written by a hook, or the runtime's own.
    private static readonly Dictionary<string, string> _notAReasonToLoad = new(StringComparer.Ordinal)
    {
        ["data-rask-hover-if"] = "on a data-rask-hover",
        ["data-rask-segment"] = "inside a data-rask-segments",
        ["data-rask-plot-area"] = "inside a data-rask-plot",
        ["data-rask-plot-row"] = "inside a data-rask-plot",
        ["data-rask-plot-tooltip"] = "inside a data-rask-plot",
        ["data-rask-drag-inset"] = "on a data-rask-drag",
        ["data-rask-focus-target"] = "inside a data-rask-focus-follows",
        ["data-rask-carousel-track"] = "inside a data-rask-carousel",
        ["data-rask-carousel-indicators"] = "inside a data-rask-carousel or a data-rask-carousel-controls",
        ["data-rask-dismiss-after"] = "the runtime's own countdown (rask-dom.ts); a data-rask-dismiss-scope holds it",
        ["data-rask-measuring"] = "written by the stack hook",
        ["data-rask-locked"] = "written by the lock hook",
        ["data-rask-managed"] = "the morph's own mark",
    };

    [Fact]
    public void Every_attribute_a_hook_module_names_is_one_the_loader_loads_the_bundle_for()
    {
        var known = LoaderAttributes().ToHashSet(StringComparer.Ordinal);

        var unknown = HookModules()
            .SelectMany(module => DataRaskName().Matches(File.ReadAllText(Path.Combine(_resources, module)))
                .Select(match => (Module: module, Attribute: match.Value)))
            .Where(named => !known.Contains(named.Attribute) && !_notAReasonToLoad.ContainsKey(named.Attribute))
            .Select(named => $"{named.Module}: {named.Attribute}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unknown.Count == 0,
            "A hook module names an attribute rask-hook-loader.ts does not list, so a page carrying only that "
            + "attribute never loads the hooks. Add it to HOOK_ATTRIBUTES (and to HookBundleTag.Attributes), or to "
            + "this test's list of attributes that are not a reason to load, with why:" + Environment.NewLine
            + string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    public void Every_attribute_the_loader_lists_is_named_by_a_hook_module()
    {
        var named = string.Concat(HookModules().Select(module => File.ReadAllText(Path.Combine(_resources, module))));

        var stale = LoaderAttributes().Where(attribute => !named.Contains(attribute, StringComparison.Ordinal)).ToList();

        Assert.True(
            stale.Count == 0,
            "rask-hook-loader.ts loads the hooks for an attribute no hook module mentions: " + string.Join(", ", stale));
    }

    [Fact]
    public void The_server_writes_the_bundles_tag_for_exactly_the_attributes_the_loader_lists()
    {
        var server = File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Server", "Http", "HookBundleTag.cs"));
        var list = server[server.IndexOf("string[] Attributes", StringComparison.Ordinal)..];

        var written = QuotedName().Matches(list[..list.IndexOf("];", StringComparison.Ordinal)]).Select(m => m.Groups[1].Value);

        Assert.Equal(LoaderAttributes().Order(StringComparer.Ordinal), written.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("src/Rask.Server/Resources/rask.ts")]
    [InlineData("src/Rask.Wasm/Resources/rask.wasm.ts")]
    public void No_module_a_runtime_imports_is_a_hook_module(string entry)
    {
        var hooks = HookModules().ToHashSet(StringComparer.Ordinal);

        var reached = Reachable(Path.Combine(_repoRoot, entry)).Select(Path.GetFileName).ToList();

        Assert.Contains("rask-hook-loader.ts", reached);
        Assert.DoesNotContain(reached, module => hooks.Contains(module!) || string.Equals(module, "rask-hooks.ts", StringComparison.Ordinal));
    }

    // The modules rask-hooks.ts imports for their side effects: the hooks, and nothing else.
    private static List<string> HookModules() =>
        [.. SideEffectImport().Matches(File.ReadAllText(Path.Combine(_resources, "rask-hooks.ts")))
            .Select(match => match.Groups[1].Value + ".ts")];

    // HOOK_ATTRIBUTES, read the way the module builds it: the data-rask-… names without their prefix, then the rest.
    private static List<string> LoaderAttributes()
    {
        var loader = File.ReadAllText(Path.Combine(_resources, "rask-hook-loader.ts"));
        var list = loader[loader.IndexOf("export const HOOK_ATTRIBUTES", StringComparison.Ordinal)..];
        var prefixed = list[..list.IndexOf(").split(\" \")", StringComparison.Ordinal)];
        var rest = list[list.IndexOf(".concat(", StringComparison.Ordinal)..];
        rest = rest[..rest.IndexOf(".split(\" \"));", StringComparison.Ordinal)];

        List<string> attributes =
        [
            .. Words(prefixed).Select(name => "data-rask-" + name),
            .. Words(rest),
        ];

        Assert.True(attributes.Count > 30, "HOOK_ATTRIBUTES is no longer written the way this test reads it.");
        return attributes;
    }

    private static IEnumerable<string> Words(string source) =>
        StringLiteral().Matches(CommentToEndOfLine().Replace(source, string.Empty))
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static HashSet<string> Reachable(string entry)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([Path.GetFullPath(entry)]);
        while (pending.TryPop(out var file))
        {
            if (!seen.Add(file) || !File.Exists(file))
            {
                continue;
            }

            foreach (Match import in ImportedModule().Matches(File.ReadAllText(file)))
            {
                var relative = import.Groups[1].Value;
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, relative[..^".js".Length] + ".ts")));
            }
        }

        return seen;
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }

    [GeneratedRegex("data-rask-[a-z]+(?:-[a-z]+)*")]
    private static partial Regex DataRaskName();

    [GeneratedRegex("""^import "\./(rask-[a-z-]+)\.js";""", RegexOptions.Multiline)]
    private static partial Regex SideEffectImport();

    [GeneratedRegex("""(?:from|import)\s+"(\.{1,2}/[^"]+\.js)";""")]
    private static partial Regex ImportedModule();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex StringLiteral();

    [GeneratedRegex("\"([a-z-]+)\"")]
    private static partial Regex QuotedName();

    [GeneratedRegex("//[^\n]*")]
    private static partial Regex CommentToEndOfLine();
}
