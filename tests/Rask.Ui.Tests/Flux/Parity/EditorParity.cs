using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/editor</c>, example for example, as the page loads: toolbar at rest, popovers
///     closed, the content as it is first painted.
/// </summary>
/// <remarks>
///     <para>
///     A parity page runs no script, and needs none: the editor renders its first paint itself — the value's
///     HTML, or the empty paragraph that carries the placeholder — which is what Flux's page shows once Tiptap
///     has drawn the same document. What only exists while the editor is USED (a pressed button, an open
///     list, the link panel, every kind of node a document can hold) is proved by
///     <c>node scripts/flux/parity-editor.mjs</c>, which mounts the kit's engine on this same page and drives
///     it and Flux's live editor through one scenario.
///     </para>
///     <para>
///     Stand-ins: the <c>flux:dropdown</c> and <c>flux:menu</c> of the "customization" example are another
///     page's, not rebuilt yet.
///     </para>
/// </remarks>
public sealed partial class EditorParity : FluxParity
{
    /// <summary>The first example's content, as Flux's editor answers it.</summary>
    internal const string ReleaseNotes =
        "<h3>What's changed</h3><ul><li><p>Markdown notation support</p></li><li><p>Accessible toolbar</p></li>"
        + "<li><p>Shortcut keys</p></li></ul><p><strong>Full changelog:</strong> "
        + "<a target=\"_blank\" rel=\"noopener noreferrer nofollow\" href=\"https://github.com/livewire/flux/compare/v1.0.25...v1.0.26\">v1.0.25...v1.0.26</a></p>";

    /// <summary>
    ///     What Flux's examples hand to <c>class</c> — an app's own utilities, stated under names of the page's:
    ///     <c>**:data-[slot=content]:min-h-[100px]!</c>, the copy button's <c>size-5!</c> and <c>hidden</c>.
    /// </summary>
    internal const string AppClasses =
        "<style>.parity-short [data-slot=content]{min-height:100px!important}"
        + ".parity-size-5{width:20px!important;height:20px!important}.parity-hidden{display:none}"
        // The docs site's accent, which a link in the document takes: blue, where the kit's own default is zinc.
        + ".parity-accent{--color-fx-accent-content:rgb(37 99 235)}.dark .parity-accent{--color-fx-accent-content:rgb(103 145 253)}</style>";

    public override string Page => "editor";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Class("parity-accent").Style("width:560px;margin:0 auto")[
            Raw.Value(AppClasses),
            Ui.Editor.Label("Release notes").Description("Explain what's new in this release.").Value(ReleaseNotes)
        ]);

        yield return ("configuring-items", Narrow(
            Ui.Editor.Toolbar("heading | bold italic underline | align ~ undo redo").Placeholder("Write something...").Class("parity-short")));

        // Flux resolves `copy` to a Blade file of the app's; here the app's item is composed into the toolbar.
        yield return ("custom-items", Narrow(
            Ui.Editor.Placeholder("Write something...").Class("parity-short")[
                Ui.EditorToolbar[
                    Standard(),
                    Ui.EditorSpacer,
                    Ui.Tooltip.Content("Copy to clipboard").Class("contents")[
                        Ui.EditorButton[
                            Ui.Icon.Name(Ui.IconName.Clipboard).Outline.Class("parity-size-5"),
                            Ui.Icon.Name(Ui.IconName.ClipboardDocumentCheck).Outline.Class("parity-hidden parity-size-5")
                        ]
                    ]
                ],
                Ui.EditorContent
            ]));

        yield return ("customization", Narrow(
            Ui.Editor.Placeholder("Write something...").Class("parity-short")[
                Ui.EditorToolbar[
                    Standard(),
                    Ui.EditorSpacer,
                    // flux:dropdown and flux:menu are not rebuilt: the wrapper is held to its place, the menu is a box.
                    Div.Attributes(("data-ui-dropdown", ""), ("data-parity-skip", "self")).Style("display:flex")[
                        Ui.EditorButton.Icon(Ui.IconName.EllipsisHorizontal).Tooltip("More"),
                        Div.Attributes(("data-ui-menu", ""), ("data-parity-skip", "")).Style("display:none")
                    ]
                ],
                Ui.EditorContent
            ]));

        yield return ("height", Narrow(Ui.Editor.Placeholder("Write something...").Class("parity-short")));
    }

    /// <summary>The default toolbar, composed part by part as Flux's "customization" example writes it.</summary>
    private static Component[] Standard() =>
    [
        Ui.EditorHeading, Ui.EditorSeparator,
        Ui.EditorBold, Ui.EditorItalic, Ui.EditorStrike, Ui.EditorSeparator,
        Ui.EditorBullet, Ui.EditorOrdered, Ui.EditorBlockquote, Ui.EditorSeparator,
        Ui.EditorLink, Ui.EditorSeparator,
        Ui.EditorAlign,
    ];

    private static Component Narrow(Component editor) => Div.Style("width:480px;margin:0 auto")[editor];
}
