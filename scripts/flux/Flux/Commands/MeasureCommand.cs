namespace Rask.Flux.Commands;

/// <summary>
///     Measures Flux UI's live documentation examples, so a Rask.Ui component can be held to them exactly.
/// </summary>
/// <remarks>
///     Rask.Ui must look and behave as Flux does, and is written without Flux's source. What makes that possible is that
///     the docs render every example live: this opens a docs page in Chromium and records, for each example, what the
///     browser computed — boxes, type, colours, borders, radii, shadows — in light and in dark, plus how each control
///     changes when hovered, pressed and keyboard-focused. Numbers, not markup: no class attribute is recorded.
///     <code>
///     flux measure &lt;out-dir&gt; &lt;page&gt;...
///     flux measure /tmp/flux components/button layouts/sidebar
///     </code>
/// </remarks>
[FluxCommand("measure", "Flux's docs pages, measured into <out-dir>")]
public sealed class MeasureCommand : IFluxCommand
{
    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: flux measure <out-dir> <page>...   (e.g. components/button)");
            return 1;
        }

        var (output, pages) = (args[0], args[1..]);
        var browser = await Browsers.Chromium();
        foreach (var path in pages)
        {
            var dir = Path.Combine(output, ReplaceFirst(path, "/", "-"));
            var schemes = await Measure.MeasurePage(browser, $"https://fluxui.dev/{path}", dir);
            Directory.CreateDirectory(dir);
            var measured = new System.Text.Json.Nodes.JsonObject { ["page"] = path, ["schemes"] = schemes };
            await File.WriteAllTextAsync(Path.Combine(dir, "measurements.json"), Js.Stringify(measured, 1));
            Console.WriteLine($"flux measure: {path} — {(schemes["light"] as System.Text.Json.Nodes.JsonArray)?.Count ?? 0} examples -> {dir}");
        }

        await browser.CloseAsync();
        return 0;
    }

    /// <summary><c>text.replace(find, with)</c>: the first occurrence only, as JavaScript's string replace.</summary>
    public static string ReplaceFirst(string text, string find, string with)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + with + text[(at + find.Length)..];
    }
}
