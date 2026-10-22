namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleCustomIconsExample : Component
{
    // Lucide's shrink and expand, as Flux's page imports them: each drawn for its own state with group-data-checked.
    private const string Lucide =
        " xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\""
        + " stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">";

    private const string Shrink =
        "<svg class=\"size-5 hidden group-data-checked:block text-accent-content\"" + Lucide
        + "<path d=\"m15 15 6 6m-6-6v4.8m0-4.8h4.8\"/><path d=\"M9 19.8V15m0 0H4.2M9 15l-6 6\"/>"
        + "<path d=\"M15 4.2V9m0 0h4.8M15 9l6-6\"/><path d=\"M9 4.2V9m0 0H4.2M9 9 3 3\"/></svg>";

    private const string Expand =
        "<svg class=\"size-5 group-data-checked:hidden text-zinc-500/85 dark:text-zinc-300/80\"" + Lucide
        + "<path d=\"m15 15 6 6\"/><path d=\"m15 9 6-6\"/><path d=\"M21 16v5h-5\"/><path d=\"M21 8V3h-5\"/>"
        + "<path d=\"M3 16v5h5\"/><path d=\"m3 21 6-6\"/><path d=\"M3 8V3h5\"/><path d=\"M9 9 3 3\"/></svg>";

    protected override Component? Render() =>
        Ui.Toggle.Icon(Raw.Value(Shrink + Expand)).Tooltip("Toggle fullscreen");
}
