namespace Rask;

// Bound to a member that holds the day as something else (UiDatePickerMoments, UiDatePickerDays): a DateTime, a value
// object. What `.Bind(…)` bound is kept as `.Choices(…)` is — no prop holds it — and ignored once a render binds a
// DateOnly.
public sealed partial class UiDatePicker : ICarriesHandSteps
{
    private UiPickerBinding<DateOnly>? _member;

    private protected override UiPickerBinding<DateOnly> Binding => Bind is null && _member is { } member ? member : base.Binding;

    /// <summary>Binds the picker to <paramref name="member" />.</summary>
    internal UiDatePicker BindMember(UiPickerBinding<DateOnly> member)
    {
        _member = member;
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiDatePicker other && _member is { } member)
        {
            other._member = member;
        }
    }
}
