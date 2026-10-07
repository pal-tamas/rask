using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/pillbox</c>, example by example.</summary>
/// <remarks>
///     <para>
///     Nine examples are rendered on Flux's page; "Custom search placeholder" shows code only. The tool names an
///     example by the last <c>&lt;h2&gt;</c> above it, which is why the backend search sits under
///     "create-option" and the last two under "loading-message".
///     </para>
///     <para>
///     The words are the rendered page's: its last example is a plain pillbox with a placeholder, where the
///     markdown shows a combobox. <c>scripts/flux/parity-pillbox.mjs</c> opens every list here and on Flux's
///     page, picks two options and compares what is then on screen.
///     </para>
/// </remarks>
public partial class PillboxParity : FluxParity
{
    // The column Flux's docs page sets every pillbox example in, and the pillbox's own width inside it.
    private const string Column = "display:flex;justify-content:center;max-width:384px;margin:0 auto";

    // The row in "With icons": flex items-center gap-2, and the icon's text-zinc-400.
    private const string AppUtilities =
        "<style>.parity-row{display:flex;align-items:center;gap:8px}.parity-icon{color:oklch(70.5% .015 286.067)}</style>";

    private static readonly (string Value, string Name)[] Tags =
    [
        ("design", "Design"), ("development", "Development"), ("marketing", "Marketing"), ("sales", "Sales"),
        ("support", "Support"), ("engineering", "Engineering"), ("product", "Product"), ("operations", "Operations"),
    ];

    private static readonly (string Value, string Name)[] Skills =
    [
        ("javascript", "JavaScript"), ("typescript", "TypeScript"), ("php", "PHP"), ("python", "Python"), ("ruby", "Ruby"),
        ("go", "Go"), ("rust", "Rust"), ("java", "Java"), ("csharp", "C#"), ("swift", "Swift"),
    ];

    private static readonly (string Value, string Name, Ui.IconName Icon)[] Platforms =
    [
        ("github", "GitHub", Ui.IconName.CodeBracket), ("gitlab", "GitLab", Ui.IconName.Server),
        ("bitbucket", "Bitbucket", Ui.IconName.Cloud),
    ];

    public override string Page => "pillbox";

    /// <summary>Whether every example holds its second and third option: the page the open list is measured on.</summary>
    protected virtual bool Picked => false;

    private List<string> Held((string Value, string Name)[] options) => Picked ? [options[1].Value, options[2].Value] : [];

    private List<int> HeldNumbers => Picked ? [2, 3] : [];

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Example(320, Ui.Pillbox.Values(Held(Tags)).Placeholder("Choose tags...")[Options(Tags)]));

        yield return ("small", Example(256, Ui.Pillbox.Values(Held(Tags)).Sm.Placeholder("Choose tags...")[Options(Tags)]));

        yield return ("searchable", Example(320, Ui.Pillbox.Values(Held(Skills)).Searchable().Placeholder("Choose skills...")[Options(Skills)]));

        yield return ("with-icons", Example(320, Raw.Value(AppUtilities), Ui.Pillbox.Values(Picked ? ["gitlab", "bitbucket"] : new List<string>()).Placeholder("Choose platforms...")[
            Platforms.Select(platform => (Component)Ui.PillboxOption.Key(platform.Value).Value(platform.Value)[
                Div.Class("parity-row")[Ui.Icon.Name(platform.Icon).Mini.Class("parity-icon"), " " + platform.Name]
            ])
        ]));

        yield return ("combobox", Example(320, Ui.Pillbox.Values(Held(Skills)).Combobox.Placeholder("Choose skills...")[Options(Skills)]));

        yield return ("create-option", Example(320, Creates(filter: null, "Create new \"")));

        yield return ("create-option", Example(320, Creates(filter: false, "Create \"")));

        yield return ("loading-message", Example(320, Creates(filter: null, "Create \"")));

        // The modal the "Create new" row opens is the modal page's to mirror: its place is held, and nothing more.
        yield return ("loading-message", Example(
            320,
            Ui.Pillbox.Values(HeldNumbers).Placeholder("Choose tags...")[
                Ui.PillboxOptionCreate["Create new"],
                Numbered()
            ],
            Div.Style("display:contents").Attributes(("data-ui-modal", ""), ("data-parity-skip", ""))));
    }

    private static Component Example(int width, params Component[] content) =>
        Div.Style(Column)[Div.Style(FormattableString.Invariant($"width:100%;max-width:{width}px"))[content]];

    private static IEnumerable<Component> Options((string Value, string Name)[] options) =>
        options.Select(option => (Component)Ui.PillboxOption.Key(option.Value).Value(option.Value)[option.Name]);

    private static IEnumerable<Component> Numbered() =>
        Tags.Select((tag, index) => (Component)Ui.PillboxOption.Key(index + 1).Value(index + 1)[tag.Name]);

    private Component Creates(bool? filter, string words) =>
        Ui.Pillbox.Values(HeldNumbers).Combobox.Filter(filter)[
            Ui.PillboxInput.Placeholder("Choose tags..."),
            Numbered(),
            Ui.PillboxOptionCreate.MinLength(2)[words, Span, "\""]
        ];
}
