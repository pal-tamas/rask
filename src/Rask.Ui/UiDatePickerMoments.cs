using System.Linq.Expressions;
using Rask.Values;

namespace Rask;

/// <summary>
///     <c>Ui.DatePicker.Bind(() =&gt; m.At)</c> over a moment the model holds as its day: a <c>DateTime</c>, a
///     <c>DateTime?</c>, or a <see cref="Moment" /> value object.
/// </summary>
/// <remarks>
///     A new day keeps the time of day the moment had — seconds and its <c>Kind</c> too — so a moment held as a day
///     (midnight) stays one, and a first day is midnight. Emptied, a <c>DateTime?</c> or a value object is null; a
///     <c>DateTime</c> cannot be, and says at once that it must be given. The member is the field: its label, the
///     model's rules and messages under the picker. A value object is made with its generated <c>TryCreate</c>, so a day
///     its rules refuse is said under the picker and not written, and the calendar offers no day outside its bounds.
/// </remarks>
public static class UiDatePickerMoments
{
    extension(RaskSeed_UiDatePicker entry)
    {
        /// <summary>Binds the day of a moment.</summary>
        /// <param name="bind">The model's moment: <c>() =&gt; m.At</c>.</param>
        /// <returns>The picker, bound to it.</returns>
        public UiDatePicker Bind(Expression<Func<DateTime>> bind) => entry.Value(default).BindMember(UiDayMembers.Moment(bind));

        /// <summary>Binds the day of a moment that may be unset.</summary>
        /// <param name="bind">The model's moment: <c>() =&gt; m.At</c>.</param>
        /// <returns>The picker, bound to it.</returns>
        public UiDatePicker Bind(Expression<Func<DateTime?>> bind) => entry.Value(default).BindMember(UiDayMembers.Moment(bind));

        /// <summary>Binds the day of a moment value object, which may be unset.</summary>
        /// <param name="bind">The model's moment: <c>() =&gt; m.FueledAt</c>.</param>
        /// <typeparam name="TMoment">The value object.</typeparam>
        /// <returns>The picker, bound to it.</returns>
        public UiDatePicker Bind<TMoment>(Expression<Func<TMoment?>> bind)
            where TMoment : Moment =>
            entry.Value(default).BindMember(UiDayMembers.Moment(bind));
    }
}
