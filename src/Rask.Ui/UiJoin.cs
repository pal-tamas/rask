namespace Rask.Ui;

/// <summary>
/// Controls joined into one continuous group.
/// </summary>
/// <remarks>
/// Each child needs <c>join-item</c> to lose its own outer corners; daisyUI cannot add it from the
/// container, so a child that looks detached is usually missing it.
/// </remarks>
public sealed partial class UiJoin : Div
{

    /// <summary>Stacks the items instead of running them across.</summary>
    public bool? Vertical { get; set; }

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "join",
            Vertical == true ? "join-vertical" : "join-horizontal",
            Class);
}
