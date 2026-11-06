namespace Rask;

/// <summary>
///     What a page says, inside the app's frame, to a signed-in person whose account may not open it: a danger callout
///     with one sentence — <c>Ui.Forbidden</c>.
/// </summary>
/// <remarks>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-06), drawn as Flux's <c>flux:callout</c>
///     <c>variant="danger"</c> with its <c>x-circle</c> icon. It is what stands in the outlet when a page's
///     <c>[Authorize]</c> turns the reader away and the kit is on; a page that decides for itself draws it in place of
///     its content: <c>!Allowed ? Ui.Forbidden : …</c>.
///     </para>
///     <para>
///     An alert, as the built-in sentence always was: it appears because the reader went somewhere they may not.
///     The words are the catalog's <c>PageForbidden</c> — „Nincs jogosultsága az oldal megtekintéséhez.” in Hungarian —
///     the sentence a refused save says in its toast too (<c>RaskStrings.Get(RaskString.PageForbidden, …)</c>).
///     </para>
/// </remarks>
public sealed partial class UiForbidden : Component
{
    /// <summary>Classes for the call site, added to the callout's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Role("alert").Class(Class)
            .Heading(RaskStrings.Get(RaskString.PageForbidden, "You do not have permission to view this page."));
}
