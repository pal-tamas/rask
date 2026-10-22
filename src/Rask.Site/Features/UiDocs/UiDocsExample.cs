namespace Rask.Site.Features.UiDocs;

/// <summary>
///     One example on a UI tab page, as fluxui.dev shows one: its name, a line on what it shows, then its own short
///     code with the live result right below it.
/// </summary>
/// <remarks>Each example is a class of its own, so the code shown is that example's and nothing else.</remarks>
internal sealed partial class UiDocsExample : Component
{
    /// <summary>What the example is called — Flux's name for it, where it has one.</summary>
    public required string Heading { get; set; }

    /// <summary>The example's source file: <c>"ButtonVariantsExample.cs"</c>.</summary>
    public required string File { get; set; }

    /// <summary>The example itself.</summary>
    public required Component Demo { get; set; }

    /// <summary>A line on what it shows. A span between backticks is code.</summary>
    public string? Blurb { get; set; }

    /// <summary>What the docs say after the result, where Flux's say more under the demo. A span between backticks is code.</summary>
    public IReadOnlyList<string>? After { get; set; }

    /// <summary>A name the browser tests find the result by, on a box around the result alone.</summary>
    public string? Testid { get; set; }

    /// <summary>Whether it sits inside a section of the page, so its title is one level below the section's.</summary>
    public bool? Nested { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Section.Class("mt-10 space-y-3")[
            Ui.Heading.Level(Nested == true ? 3 : 2).Lg[Heading],
            Blurb is null ? null : Ui.Text[UiDocsProse.Of(Blurb)],
            CodeSample.Files([File]).Result(Testid is null ? Demo : Div.Data("testid", Testid)[Demo]),
            After?.Select((paragraph, index) => Ui.Text.Key(index)[UiDocsProse.Of(paragraph)])
        ];
}
