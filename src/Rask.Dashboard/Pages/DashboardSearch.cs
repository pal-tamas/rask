namespace Rask.Dashboard.Pages;

/// <summary>The search box above a panel's table: Flux's input, with the magnifying glass and its placeholder as its name.</summary>
internal sealed partial class DashboardSearch : Component
{
    public string? Value { get; set; }

    public required string Placeholder { get; set; }

    public Callback<string> OnChange { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Input
            .Value(Value ?? string.Empty)
            .Type(InputType.Search)
            .Icon(Ui.IconName.MagnifyingGlass)
            .Placeholder(Placeholder)
            .Attributes(("aria-label", Placeholder))
            .OnChange(ChangedAsync);

    private Task ChangedAsync(string value) => OnChange.Invoke(value).AsTask();
}
