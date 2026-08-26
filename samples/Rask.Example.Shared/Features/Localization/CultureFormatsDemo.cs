using System.Globalization;

namespace Rask.Example.Shared.Features.Localization;

/// <summary>
///     The same three values, formatted for four languages side by side.
/// </summary>
/// <remarks>
///     <para>
///         Two rules make this demo safe to snapshot, and both are load-bearing:
///     </para>
///     <para>
///         <b>Fixed sample values, never <c>DateTime.Now</c>.</b> Every registered demo's markup
///         skeleton is committed as a golden file, and the guide page is screenshotted — a value that
///         moves would make both go stale on a schedule rather than when something actually changed.
///     </para>
///     <para>
///         <b>An explicit <c>CultureInfo</c> per call, never an assignment to
///         <c>CultureInfo.CurrentCulture</c>.</b> A guide page mounts many demos in one render; setting
///         the ambient culture here would reformat every other demo on the page, and the golden would
///         record it.
///     </para>
/// </remarks>
public sealed partial class CultureFormatsDemo : Component
{
    private static readonly DateTimeOffset Sample = new(2026, 3, 14, 15, 9, 26, TimeSpan.Zero);
    private const decimal Price = 1234.56m;
    private const double Ratio = 0.4567;

    private static readonly string[] Languages = ["en-US", "hu-HU", "de-DE", "ar-EG"];

    protected override Component Render()
    {
        // A runtime built without culture data formats everything identically, which would make this
        // table look broken rather than instructive. Say so instead of showing four identical columns.
        if (!Rask.Core.Globalization.RaskCultureResolver.IsGlobalizationSupported)
        {
            return Div.Class("alert alert-warning")[
                "This build runs with ",
                Code["InvariantGlobalization"],
                ", so every culture formats identically. Set ",
                Code["<RaskGlobalization>true</RaskGlobalization>"],
                " to ship ICU — see the WASM section of this guide."
            ];
        }

        return Table.Class("table table-sm align-middle")[
            Thead[
                Tr[
                    Th["Language"],
                    Th["Date"],
                    Th["Money"],
                    Th["Percent"]
                ]
            ],
            // The enumerable is passed straight in: a `..` spread does not bind against the chain's
            // children indexer.
            Tbody[Languages.Select(Row)]
        ];
    }

    private static Component Row(string tag)
    {
        var culture = CultureInfo.GetCultureInfo(tag);

        // The tag goes in a data attribute, never a class: the golden records tag names and sorted
        // class tokens, so a culture in a class would bake the sample data into the snapshot.
        return Tr.Data(new Dictionary<string, string?> { ["culture"] = tag })[
            Td[Code[tag]],
            Td[Sample.ToString("d", culture)],
            Td[Price.ToString("C", culture)],
            Td[Ratio.ToString("P1", culture)]
        ];
    }
}
