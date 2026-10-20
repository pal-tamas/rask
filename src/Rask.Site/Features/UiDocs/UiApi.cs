using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>The reference tables of the UI tab's component pages: each part, and the steps it has of its own.</summary>
/// <remarks>
///     Written from the components' own doc comments. <c>UiApiReferenceTests</c> fails when a component gains a step
///     that is not listed here, or loses one that is.
/// </remarks>
internal static class UiApi
{
    private const string Hue = "Ui.Color: one of Tailwind's hues, each a step too (.Blue)";

    public static IReadOnlyList<UiApiPart> Badge { get; } =
    [
        new("Ui.Badge", typeof(UiBadge), "flux:badge", "A status, a category or a count. Its children are its words.",
        [
            new("Color", typeof(Ui.Color?), "Its colour. Zinc when unset.", Hue),
            new("Size", typeof(Ui.BadgeSize?), "How big it is. Base when unset."),
            new("Variant", typeof(Ui.BadgeVariant?), "How it is filled. Soft when unset."),
            new("Rounded", typeof(bool?), "Rounds the ends fully, with the wider padding a round end needs."),
            new("Icon", typeof(Ui.IconName?), "An icon before the words."),
            new("IconTrailing", typeof(Ui.IconName?), "An icon after the words."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icons. Micro, the size of the line, when unset."),
            new("As", typeof(Ui.BadgeAs?), "The element it is: a button to press with OnClick. A div when unset."),
            new("Inset", typeof(Ui.Inset?), "The sides whose padding is taken back, for a badge in a line of text."),
        ]),
        new("Ui.BadgeClose", typeof(UiBadgeClose), "flux:badge.close", "The button inside a badge that removes it. Its OnClick does the removing.",
        [
            new("Icon", typeof(Ui.IconName?), "The icon it shows. XMark when unset."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Micro when unset."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Button { get; } =
    [
        new("Ui.Button", typeof(UiButton), "flux:button", "A <button>, or an <a> given Href. Its children are its label.",
        [
            new("Variant", typeof(Ui.ButtonVariant?), "How it is drawn. Outline when unset."),
            new("Size", typeof(Ui.ButtonSize?), "How large it is. Base, 40px tall, when unset."),
            new("Color", typeof(Ui.Color?), "A hue for every variant but Danger; a gray changes only Primary.", Hue),
            new("Icon", typeof(Ui.IconName?), "The icon before the label, or with no label the whole button."),
            new("IconTrailing", typeof(UiInputIcon?), "What stands after the label: an icon, or content of your own.", "Ui.IconName, or a component"),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icons. Micro beside a label, mini alone, when unset."),
            new("Square", typeof(bool?), "As wide as it is tall. Automatic for a button with no children."),
            new("Align", typeof(Ui.Align?), "Where the content sits in a button wider than it. Centred when unset."),
            new("Inset", typeof(Ui.Inset?), "The sides to pull outwards, so a ghost button lines up with the text around it."),
            new("Loading", typeof(bool?), "Whether it shows that it waits. Unset, it does so while its own handler runs past 200 ms."),
            new("Tooltip", typeof(string), "A hint shown on hover and focus, which also names a button with no label."),
            new("TooltipPosition", typeof(Ui.TooltipPosition?), "Which side the tooltip opens on. Above when unset."),
            new("TooltipKbd", typeof(string), "A shortcut shown at the end of the tooltip: \"⌘S\"."),
            new("Kbd", typeof(string), "A shortcut shown inside the button, after its label."),
            new("Href", typeof(RouteUrl?), "Where it goes. A generated route stays in the app; a string is a link the browser follows.", "a route, or a URL string"),
            new("Type", typeof(Ui.ButtonType?), "What a press does: Submit on a form's submit button. Button when unset."),
            new("As", typeof(Ui.ButtonAs?), "The element to render. Href makes it an <a> without this."),
            new("Disabled", typeof(bool?), "Disables it by attribute, so the browser refuses the press."),
            new("Command", typeof(string), "The invoker command run on CommandFor's element: a dialog or popover opened with no script."),
            new("CommandFor", typeof(string), "The id of the element Command acts on."),
        ]),
        new("Ui.ButtonGroup", typeof(UiButtonGroup), "flux:button.group", "Fuses the buttons inside it into one control.", []),
    ];

    public static IReadOnlyList<UiApiPart> Callout { get; } =
    [
        new("Ui.Callout", typeof(UiCallout), "flux:callout", "A message set apart from the page: an icon, a heading, text and actions.",
        [
            new("Variant", typeof(Ui.CalloutVariant?), "What it is saying. Secondary on a white surface when unset."),
            new("Color", typeof(Ui.Color?), "A hue of its own, which wins over Variant.", Hue),
            new("Icon", typeof(Ui.IconName?), "The icon beside the content. None when unset."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Mini when unset."),
            new("CustomIcon", typeof(Component), "An icon of your own in place of Icon."),
            new("Heading", typeof(string), "Shorthand for a Ui.CalloutHeading as the first child."),
            new("Text", typeof(string), "Shorthand for a Ui.CalloutText after the heading."),
            new("Actions", typeof(Component), "What the reader can do about it: buttons or links."),
            new("Inline", typeof(bool?), "Puts the actions beside the text where there is room."),
            new("Controls", typeof(Component), "What sits at the top right: a dismiss button. Dismissing is the page's."),
            new("Role", typeof(string), "The callout's role. None when unset: a callout that appears because something happened says status or alert."),
        ]),
        new("Ui.CalloutHeading", typeof(UiCalloutHeading), "flux:callout.heading", "The callout's title line.",
        [
            new("Icon", typeof(Ui.IconName?), "An icon in the heading's line, instead of beside the whole callout."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Mini when unset."),
            new("CustomIcon", typeof(Component), "An icon of your own in place of Icon."),
        ]),
        new("Ui.CalloutText", typeof(UiCalloutText), "flux:callout.text", "The callout's body copy.", []),
        new("Ui.CalloutLink", typeof(UiCalloutLink), "flux:callout.link", "A link inside the text, drawn in the callout's ink.",
        [
            new("Href", typeof(RouteUrl?), "Where it goes: a generated route to stay in the app, or a URL string to leave it.", "a route, or a URL string"),
            new("External", typeof(bool?), "Opens it in a new tab, safely. Ignored for a route of the app."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Card { get; } =
    [
        new("Ui.Card", typeof(UiCard), "flux:card", "A container for related content, with the spacing between its parts.",
        [
            new("Variant", typeof(Ui.CardVariant?), "The card's surface."),
            new("Size", typeof(Ui.CardSize?), "Scales the padding, the corners and the spacing."),
            new("Body", typeof(Ui.CardBodyVariant?), "How the header, body and footer are set apart. Seamless when unset."),
            new("Divider", typeof(Ui.CardDivider?), "With Divided, where the lines stop."),
            new("Highlight", typeof(bool?), "A faint highlight inside the top edge in light mode. On unless false."),
        ]),
        new("Ui.CardHeader", typeof(UiCardHeader), "flux:card.header", "The heading, subheading and actions across the top.",
        [
            new("Size", typeof(Ui.CardSize?), "Outside a card, the size of the card it sits beside."),
        ]),
        new("Ui.CardHeading", typeof(UiCardHeading), "flux:card.heading", "The card's title.",
        [
            new("Size", typeof(Ui.CardHeadingSize?), "How big it looks."),
            new("Level", typeof(int?), "The heading level to render: 2 for an <h2>."),
        ]),
        new("Ui.CardSubheading", typeof(UiCardSubheading), "flux:card.subheading", "The line under the heading.", []),
        new("Ui.CardActions", typeof(UiCardActions), "flux:card.actions", "Buttons at the end of a header or footer.", []),
        new("Ui.CardBody", typeof(UiCardBody), "flux:card.body", "The card's content.", []),
        new("Ui.CardFooter", typeof(UiCardFooter), "flux:card.footer", "The line along the bottom.",
        [
            new("Size", typeof(Ui.CardSize?), "Outside a card, the size of the card it sits beside."),
        ]),
        new("Ui.CardBleed", typeof(UiCardBleed), "flux:card.bleed", "Content that runs to the card's edges: an image.", []),
    ];

    public static IReadOnlyList<UiApiPart> Heading { get; } =
    [
        new("Ui.Heading", typeof(UiHeading), "flux:heading", "A title. Its size and its place in the outline are separate.",
        [
            new("Size", typeof(Ui.HeadingSize?), "How big it looks. Base when unset."),
            new("Level", typeof(int?), "The heading level, 1 to 4. Unset, it is a <div> outside the outline."),
            new("Accent", typeof(bool?), "Draws it in the accent colour."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Icon { get; } =
    [
        new("Ui.Icon", typeof(UiIcon), "flux:icon.*", "One of the Heroicons, as an inline SVG.",
        [
            new("Name", typeof(Ui.IconName), "Which icon to draw. Required, and taken first."),
            new("Variant", typeof(Ui.IconVariant?), "Which drawing of it, and so how large. Outline when unset."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Separator { get; } =
    [
        new("Ui.Separator", typeof(UiSeparator), "flux:separator", "A line between sections, or between items in a row. It carries no margin.",
        [
            new("Vertical", typeof(bool?), "Runs down instead of across."),
            new("Orientation", typeof(Ui.SeparatorOrientation?), "The other way to say Vertical, for a direction decided at run time."),
            new("Variant", typeof(Ui.SeparatorVariant?), "How strongly the line is drawn."),
            new("Text", typeof(string), "A word in the middle of the line: \"or\"."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Text { get; } =
    [
        new("Ui.Text", typeof(UiText), "flux:text", "Body copy: a paragraph, or a span with Inline.",
        [
            new("Size", typeof(Ui.TextSize?), "How big it looks. Default when unset."),
            new("Variant", typeof(Ui.TextVariant?), "How much it stands out. Default when unset."),
            new("Color", typeof(Ui.Color?), "One of Tailwind's hues in place of the variant's ink.", Hue),
            new("Inline", typeof(bool?), "A <span> rather than a paragraph."),
        ]),
        new("Ui.Link", typeof(UiLink), "flux:link", "A link in the accent colour, underlined.",
        [
            new("Href", typeof(RouteUrl?), "Where it goes: a generated route to stay in the app, or a URL string to leave it.", "a route, or a URL string"),
            new("Variant", typeof(Ui.LinkVariant?), "How it shows that it is a link. Default when unset."),
            new("External", typeof(bool?), "Opens it in a new tab with rel=\"noopener noreferrer\". Ignored for a route."),
            new("As", typeof(Ui.LinkAs?), "The element it renders: a <button> drawn as a link. A when unset."),
            new("Accent", typeof(bool?), "Whether it takes the accent colour. On unless false."),
        ]),
    ];

    /// <summary>Every page's parts, for the tests that hold them to the components.</summary>
    public static IEnumerable<UiApiPart> All =>
        new[] { Badge, Button, Callout, Card, Heading, Icon, Separator, Text }.SelectMany(parts => parts);
}
