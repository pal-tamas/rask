namespace Rask.Ui;

/// <summary>
/// Numbered pages, as a joined row of buttons.
/// </summary>
/// <remarks>
/// daisyUI has no pagination component of its own — it is <c>join</c> plus buttons, which is what this
/// renders. The current page is a button that is <c>disabled</c> rather than merely styled: it is not an
/// action, and letting it be pressed re-navigates to where the reader already is.
/// </remarks>
public sealed partial class UiPagination : Component
{
    public required int Pages { get; set; }

    public required int Current { get; set; }

    public Callback<int>? OnSelect { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("join", Class))
            .Aria(new Dictionary<string, string?> { ["label"] = "Pagination" })[
            Enumerable.Range(1, Math.Max(Pages, 0)).Select(page =>
            {
                var button = Button
                    .Key(page)
                    .Type("button")
                    .Class(UiClass.Compose("join-item btn", page == Current ? "btn-active" : ""))
                    .Disabled(page == Current);

                if (OnSelect is { } select && page != Current)
                {
                    button = button.OnClick(() => select.Invoke(page) ?? Task.CompletedTask);
                }

                return button[page.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            })
        ];
}
