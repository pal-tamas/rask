using System.Linq.Expressions;
using Rask.Values;

namespace Rask;

/// <summary><c>Ui.DatePicker.Bind(() =&gt; m.InvoiceDay)</c> over a <see cref="Day" /> value object.</summary>
/// <remarks>
///     The day picked is made into the value object with its generated <c>TryCreate</c>: a day its rules refuse is said
///     under the picker and not written, and the calendar offers no day outside the bounds its base declares
///     (<c>NotBefore</c>, <c>NotAfter</c>, <c>NotAfterToday</c>). Emptied, it is null. The member is the field: its
///     label, the model's rules and messages under the picker.
/// </remarks>
public static class UiDatePickerDays
{
    extension(RaskSeed_UiDatePicker entry)
    {
        /// <summary>Binds a day value object, which may be unset.</summary>
        /// <param name="bind">The model's day: <c>() =&gt; m.InvoiceDay</c>.</param>
        /// <typeparam name="TDay">The value object.</typeparam>
        /// <returns>The picker, bound to it.</returns>
        public UiDatePicker Bind<TDay>(Expression<Func<TDay?>> bind)
            where TDay : Day =>
            entry.Value(default).BindMember(UiDayMembers.Day(bind));
    }
}
