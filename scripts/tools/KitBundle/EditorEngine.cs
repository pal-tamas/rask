using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KitBundle;

/// <summary>
///     Ui.Editor's engine: <c>ui-editor.ts</c> and Tiptap into <c>Resources/ui-editor.js</c>, with
///     <c>ui-editor.LICENSES.txt</c> — every bundled package's notice, and Lucide's, whose path data the toolbar's
///     icons are drawn from in C# — and the content hash <c>UiEditorEngine.Version</c> carries.
/// </summary>
/// <remarks>
///     Tiptap follows npm's latest, every <c>@tiptap/</c> package at one release; <c>prosemirror-model</c> is held
///     (bundle.json's overrides) to the last release whose serialised HTML is what Flux's live editor answers.
///     <c>notices/</c> holds the one licence text the registry does not deliver: Lucide's, which is not a
///     dependency of the bundle at all (lucide-icons/lucide at 0.300.0). Refresh it when that pin moves.
/// </remarks>
internal sealed partial class EditorEngine : Engine
{
    private static readonly string[] LicenceFiles = ["LICENSE", "LICENSE.md", "LICENSE.txt", "license", "license.md"];

    internal override string Name => "editor";

    internal override IReadOnlyList<string> Bundles => ["ui-editor.js"];

    internal override string? Family => "@tiptap/";

    protected override string Entry => "ui-editor.ts";

    protected override async Task Bundle(string tree, IReadOnlyList<LockedPackage> locked)
    {
        var outfile = Path.Combine(Repository.Resources, "ui-editor.js");
        var metafile = Path.Combine(tree, "..", "meta.json");
        const string Banner = "/*! Rask.Ui editor engine. Bundles Tiptap and ProseMirror (MIT) — see rask-ui-editor.LICENSES.txt, beside this file. */";
        await Esbuild.Run(tree, [.. Options(outfile, Banner), "--metafile=" + Path.GetFullPath(metafile)]);

        var packages = Contributors(metafile);
        var notices = new StringBuilder("Third-party notices of Ui.Editor.\n\nThe editor engine (ui-editor.js) bundles the following packages.\n");
        foreach (var name in packages.Order(English))
        {
            notices.Append(Notice(name, Path.Combine(tree, "node_modules", name)));
        }

        notices.Append("\n\nEleven toolbar icons (Lucide's type, list-ordered, text-quote, link-2, unlink, undo, redo, subscript,\n")
            .Append("superscript, highlighter and code) are drawn by the kit's assembly, Rask.Ui.dll, from the path data of:\n")
            .Append(Section("lucide 0.300.0 — ISC", await File.ReadAllTextAsync(Path.Combine(Directory, "notices", "lucide.LICENSE"))));
        await File.WriteAllTextAsync(Path.Combine(Repository.Resources, "ui-editor.LICENSES.txt"), notices.ToString());

        var bytes = await File.ReadAllBytesAsync(outfile);
        var version = Hash(bytes);
        var source = Path.Combine(Repository.Kit, "UiEditorEngine.cs");
        await File.WriteAllTextAsync(source, WithConstant(await File.ReadAllTextAsync(source), "Version", version));
        Console.WriteLine($"ui-editor.js: {bytes.Length} bytes, {packages.Count} packages, version {version}");
    }

    // Every package that contributed bytes to the bundle, by the module paths esbuild's metafile records.
    private static HashSet<string> Contributors(string metafile)
    {
        using var meta = JsonDocument.Parse(File.ReadAllText(metafile));
        var output = meta.RootElement.GetProperty("outputs").EnumerateObject().First().Value;
        return output.GetProperty("inputs").EnumerateObject()
            .Select(input => Package().Match(input.Name))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Notice(string name, string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "package.json")));
        var version = manifest.RootElement.GetProperty("version").GetString();
        var license = manifest.RootElement.TryGetProperty("license", out var l) ? l.ToString() : "UNLICENSED";
        var text = LicenceFiles.Select(file => Path.Combine(directory, file)).Where(File.Exists).Select(File.ReadAllText).FirstOrDefault(t => t.Length > 0)
                   ?? throw new KitBundleException($"{name} {version} ({license}) ships no licence file: add its text to notices/ and name it here.");
        return Section($"{name} {version} — {license}", text);
    }

    [GeneratedRegex("node_modules/((?:@[^/]+/)?[^/]+)/")]
    private static partial Regex Package();
}
