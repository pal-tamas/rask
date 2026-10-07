using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>The validation message of a form control.</summary>
/// <remarks>
/// <para>
/// Flux UI's error. Inside a <see cref="UiField" /> a bare <c>Ui.Error</c> shows the first message of the
/// field's bound control. Anywhere else, say which member: <c>Ui.Error.For(() =&gt; model.Email)</c>, or by
/// <see cref="Name" /> on the form's model.
/// </para>
/// <para>
/// Always in the page, hidden while there is nothing to say: it is a live region, and a screen reader only
/// announces a message that arrives in one it already knows.
/// </para>
/// </remarks>
public sealed partial class UiError : Component
{
    private const string Look = "mt-3 text-sm font-medium text-red-500 dark:text-red-400";

    private const string IconLook = "inline size-5 shrink-0";

    // Heroicons v2 (MIT), 20px solid exclamation-triangle.
    private const string Triangle =
        "M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72"
        + "c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5"
        + "A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z";

    /// <summary>The bound member whose messages to show, as the control's own <c>Bind</c> names it.</summary>
    public LambdaExpression? For { get; set; }

    /// <summary>The member of the form's model whose messages to show: <c>nameof(model.Email)</c>.</summary>
    public string? Name { get; set; }

    /// <summary>A message of your own, shown whatever the form says.</summary>
    public string? Message { get; set; }

    /// <summary>The icon beside the message. The warning triangle unless told otherwise; <c>false</c> for none.</summary>
    public UiErrorIcon? Icon { get; set; }

    /// <summary>Its id. Inside a field it is derived from the control's.</summary>
    public string? Id { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiFieldScope>();
        var custom = Children?.Any() == true;
        var message = custom ? null : Message ?? FormMessage(scope);
        var shown = custom || message is not null;
        var box = Div.Id(Id ?? scope?.ErrorId)
            .Class(UiClass.Compose(shown ? null : "hidden", Look, Class))
            .Data("ui-error", "")
            .Role("alert")
            .Aria(("live", "polite"), ("atomic", "true"));

        if (!shown)
        {
            return box;
        }

        return custom ? box[Glyph(), " ", Children!] : box[Glyph(), " ", message!];
    }

    // Reading the form's messages is what keeps this out of the render cache, as it does Validation.Message:
    // a message added after an await has to be seen by the next render.
    private string? FormMessage(UiFieldScope? scope)
    {
        if (EditContextScope.Current is not { } form)
        {
            return null;
        }

        var field = (For, Name) switch
        {
            ({ } bind, _) => ExpressionAccessor.Parse(bind).Field,
            (_, { } name) => new FieldIdentifier(form.Model, name),
            _ => scope?.Bound is { } bound ? ExpressionAccessor.Parse(bound).Field : (FieldIdentifier?)null,
        };

        return field is { } known && form.GetValidationMessages(known) is [var first, ..] ? first : null;
    }

    private Component? Glyph() => Icon switch
    {
        { Hidden: true } => null,
        { Name: { } name } => Ui.Icon.Name(name).Class(IconLook),
        _ => Svg.ViewBox("0 0 20 20")
            .Fill("currentColor")
            .Class(IconLook)
            .Attributes(("aria-hidden", "true"), ("focusable", "false"))[
            SvgPath.D(Triangle).Attributes(("fill-rule", "evenodd"), ("clip-rule", "evenodd"))
        ],
    };
}
