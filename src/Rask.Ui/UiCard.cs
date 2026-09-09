namespace Rask.Ui;

/// <summary>A bordered panel with an optional heading and an optional action in its corner.</summary>
public sealed partial class UiCard : Component
{
    // Not `Title`: that name is the <title> tag's builder entry, inherited from Component.
    public string? Heading { get; set; }

    public Component? Action { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component? header = Heading is null && Action is null
            ? null
            // Wraps rather than truncating: a card's action is often a button whose label is the only thing
            // saying what it does, and on a phone the heading and the action rarely fit on one line.
            : Div.Class("mb-4 flex flex-wrap items-center justify-between gap-3")[
                Heading is null ? null : H2.Class(UiStyles.Heading)[Heading],
                Action
            ];

        return Div.Class(UiClass.Compose(UiStyles.Card, Class))[header, Children ?? []];
    }
}
