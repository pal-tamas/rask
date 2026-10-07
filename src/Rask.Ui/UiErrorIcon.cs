namespace Rask;

/// <summary>
///     The icon beside a <see cref="UiError" /> message: one of the kit's — <c>.Icon(Ui.IconName.InformationCircle)</c> — or
///     none, <c>.Icon(false)</c>.
/// </summary>
public readonly record struct UiErrorIcon
{
    private UiErrorIcon(Ui.IconName? name, bool hidden) => (Name, Hidden) = (name, hidden);

    /// <summary>The icon to draw, or <c>null</c> for the warning triangle.</summary>
    public Ui.IconName? Name { get; }

    /// <summary>Whether the message stands without an icon.</summary>
    public bool Hidden { get; }

    /// <summary>This icon in place of the warning triangle.</summary>
    public static UiErrorIcon FromIconName(Ui.IconName name) => new(name, false);

    /// <summary><c>false</c> hides the icon; <c>true</c> is the warning triangle.</summary>
    public static UiErrorIcon FromBoolean(bool shown) => new(null, !shown);

    /// <inheritdoc cref="FromIconName" />
    public static implicit operator UiErrorIcon(Ui.IconName name) => FromIconName(name);

    /// <inheritdoc cref="FromBoolean" />
    public static implicit operator UiErrorIcon(bool shown) => FromBoolean(shown);
}
