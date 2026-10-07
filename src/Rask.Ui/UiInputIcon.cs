namespace Rask;

/// <summary>
///     What sits inside a <see cref="UiInput{T}" /> at one end: a Heroicon by name, or content of your own.
/// </summary>
/// <remarks>
///     Flux says both with one name — the <c>icon:trailing</c> prop and the <c>icon:trailing</c> slot — and so
///     does this: <c>.IconTrailing(Ui.IconName.CreditCard)</c> or <c>.IconTrailing(Ui.Button…)</c>.
/// </remarks>
public readonly record struct UiInputIcon
{
    private UiInputIcon(Ui.IconName? name, Component? content) => (Name, Content) = (name, content);

    /// <summary>The Heroicon to draw, when one was named.</summary>
    public Ui.IconName? Name { get; }

    /// <summary>The content to place instead of an icon — a button, usually.</summary>
    public Component? Content { get; }

    /// <summary>A Heroicon.</summary>
    public static UiInputIcon FromIconName(Ui.IconName name) => new(name, null);

    /// <summary>Content of your own.</summary>
    public static UiInputIcon FromComponent(Component content) => new(null, content);

    /// <inheritdoc cref="FromIconName" />
    public static implicit operator UiInputIcon(Ui.IconName name) => FromIconName(name);

    /// <inheritdoc cref="FromComponent" />
    public static implicit operator UiInputIcon(Component content) => FromComponent(content);
}
