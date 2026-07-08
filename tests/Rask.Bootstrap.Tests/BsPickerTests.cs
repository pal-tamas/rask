using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Core;

namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for the custom-popover pickers (BsDatePicker / BsTimePicker /
// BsDateTimePicker). The calendar is localized, so markup is asserted under a fixed culture. The
// popover is always in the DOM (toggled by .show like BsMultiSelect), so its grid/time markup is
// present in ToHtml(). The click→writeback path needs the live runtime and is covered by the
// showcase E2E journey (same split as BsMultiSelect, whose interaction is E2E-only).
public class BsPickerTests
{
    private static readonly DateOnly Jul7 = new(2026, 7, 7);
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string Html(CultureInfo culture, Func<Component> render)
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return render().ToHtml();
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    // ---- Date -------------------------------------------------------------------------------------

    [Fact]
    public void Date_Controlled_RendersComboboxValueAndCaret()
    {
        var html = Html(Us, () => BsDatePicker<DateOnly>(Value: Jul7, Label: "Day", Id: "d"));
        Assert.Contains("<label class=\"form-label\" for=\"d\">Day</label>", html);
        Assert.Contains("role=\"combobox\"", html);
        Assert.Contains("aria-haspopup=\"grid\"", html);
        Assert.Contains("aria-controls=\"d-cal\"", html);
        Assert.Contains("<span>7/7/2026</span>", html);
        Assert.Contains("bs-picker-caret", html);
    }

    [Fact]
    public void Date_Grid_HasGridRolesSelectedDayAndSevenHeaders()
    {
        var html = Html(Us, () => BsDatePicker<DateOnly>(Value: Jul7, Id: "d"));
        Assert.Contains("<div id=\"d-cal\" class=\"bs-cal\" role=\"grid\"", html);
        Assert.Contains("role=\"gridcell\"", html);
        // The bound day is marked selected + focused, and carries a full localized aria-label. The cell
        // also gains bs-cal-today, but only when the test happens to run on July 7 2026, so the today
        // marker is deliberately not asserted here — it would make the test fail on every other date.
        // `bs-cal-focus` is unique to the cursor cell (which is the bound cell), so this pins the right one.
        Assert.Contains("id=\"d-d-20260707\" class=\"bs-cal-cell", html);
        Assert.Contains("active bs-cal-focus\"", html);
        Assert.Contains("aria-label=\"Tuesday, July 7, 2026\" aria-selected=\"true\"", html);
        Assert.Equal(7, CountOccurrences(html, "role=\"columnheader\""));
        // Always six week rows (42 day cells) so the popover height is stable.
        Assert.Equal(42, CountOccurrences(html, "role=\"gridcell\""));
    }

    [Fact]
    public void Date_EnUs_IsSundayFirst() =>
        Assert.Contains("role=\"columnheader\" aria-label=\"Sunday\">Sun<",
            Html(Us, () => BsDatePicker<DateOnly>(Value: Jul7)));

    [Fact]
    public void Date_DeDe_IsMondayFirstAndLocalizedMonth()
    {
        var html = Html(De, () => BsDatePicker<DateOnly>(Value: Jul7));
        Assert.Contains("role=\"columnheader\" aria-label=\"Montag\">Mo<", html);
        Assert.Contains("<span class=\"fw-semibold\">Juli 2026</span>", html);
    }

    [Fact]
    public void Date_MinMax_GreysOutOfRangeDaysAndDisablesPrev()
    {
        var html = Html(Us, () =>
            BsDatePicker<DateOnly>(Value: new DateOnly(2026, 7, 15), Min: new DateOnly(2026, 7, 10), Id: "d"));
        // A day before Min is disabled + aria-disabled.
        Assert.Contains("id=\"d-d-20260705\" class=\"bs-cal-cell disabled\"", html);
        Assert.Contains("aria-label=\"Sunday, July 5, 2026\" aria-disabled=\"true\"", html);
        // The whole previous month is below Min, so the prev button is disabled.
        Assert.Contains("aria-label=\"Previous month\" type=\"button\" disabled", html);
    }

    [Fact]
    public void Date_NullableEmpty_ShowsPlaceholderAndNoClear()
    {
        var html = Html(Us, () => BsDatePicker<DateOnly?>(Value: null, Placeholder: "pick"));
        Assert.Contains("<span class=\"text-body-secondary\">pick</span>", html);
        Assert.DoesNotContain("btn-close", html);
    }

    [Fact]
    public void Date_NullableWithValue_ShowsClearButton() =>
        Assert.Contains("btn-close", Html(Us, () => BsDatePicker<DateOnly?>(Value: Jul7)));

    [Fact]
    public void Date_NonNullableWithValue_HasNoClearButton() =>
        Assert.DoesNotContain("btn-close", Html(Us, () => BsDatePicker<DateOnly>(Value: Jul7)));

    [Fact]
    public void Date_Native_RendersNativeInputNotGrid()
    {
        var html = Html(Us, () => BsDatePicker<DateOnly>(Value: Jul7, Native: true));
        Assert.Contains("type=\"date\"", html);
        Assert.Contains("value=\"2026-07-07\"", html);
        Assert.DoesNotContain("role=\"grid\"", html);
    }

    [Fact]
    public void Date_Disabled_IsNotFocusable()
    {
        var html = Html(Us, () => BsDatePicker<DateOnly>(Value: Jul7, Disabled: true));
        Assert.Contains("disabled pe-none", html);
        Assert.DoesNotContain("tabindex=\"0\"", html);
    }

    [Fact]
    public void Date_Bound_RendersModelValueAndPropertyDerivedIds()
    {
        var model = new DayModel { Day = Jul7 };
        var html = Html(Us, () => BsDatePicker(() => model.Day));
        Assert.Contains("<span>7/7/2026</span>", html);
        Assert.Contains("aria-controls=\"Day-cal\"", html); // controlId derives from the property name
        Assert.Contains("id=\"Day-d-20260707\"", html);
    }

    [Fact]
    public void Date_TwoIdlessPickers_GetUniqueGridIds()
    {
        // Two controlled pickers without an Id must not collide on grid/cell ids (else aria-controls /
        // aria-activedescendant resolve to the wrong calendar).
        var html = Html(Us, () => Div()[
            BsDatePicker<DateOnly>(Value: Jul7),
            BsDatePicker<DateOnly>(Value: Jul7)
        ]);
        var ids = Regex.Matches(html, "id=\"(bsdp\\d+)-cal\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.Equal(2, ids.Count);
    }

    // ---- Time -------------------------------------------------------------------------------------

    [Fact]
    public void Time_Controlled_RendersListboxesActiveValueAndStep()
    {
        var html = Html(Us, () => BsTimePicker<TimeOnly>(Value: new TimeOnly(9, 30), MinuteStep: 15));
        Assert.Contains("aria-haspopup=\"listbox\"", html);
        Assert.Contains("role=\"listbox\" aria-label=\"Hour\"", html);
        Assert.Contains("role=\"listbox\" aria-label=\"Minute\"", html);
        Assert.Contains("class=\"dropdown-item bs-time-item active\" data-rask-key=\"9\" aria-selected=\"true\" type=\"button\">09", html);
        Assert.Contains("aria-selected=\"true\" type=\"button\">30", html);
        // 24 hour options + (MinuteStep 15 → 00/15/30/45) four minute options.
        Assert.Equal(4, CountMinuteItems(html));
    }

    [Fact]
    public void Time_Native_RendersNativeInput() =>
        Assert.Contains("type=\"time\"",
            Html(Us, () => BsTimePicker<TimeOnly>(Value: new TimeOnly(9, 30), Native: true)));

    // ---- DateTime ---------------------------------------------------------------------------------

    [Fact]
    public void DateTime_Controlled_RendersCalendarAndTime()
    {
        var html = Html(Us, () => BsDateTimePicker<DateTime>(Value: new DateTime(2026, 7, 7, 9, 30, 0)));
        Assert.Contains("class=\"bs-datetime", html);
        Assert.Contains("role=\"grid\"", html);
        Assert.Contains("role=\"listbox\" aria-label=\"Hour\"", html);
        Assert.Contains("aria-selected=\"true\" type=\"button\">30", html);
    }

    [Fact]
    public void DateTime_Native_RendersNativeInput() =>
        Assert.Contains("type=\"datetime-local\"",
            Html(Us, () => BsDateTimePicker<DateTime>(Value: new DateTime(2026, 7, 7, 9, 30, 0), Native: true)));

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }

    // Number of minute buttons = time-items minus the 24 hour buttons.
    private static int CountMinuteItems(string html) => CountOccurrences(html, "bs-time-item") - 24;

    private sealed class DayModel
    {
        public DateOnly Day { get; set; }
    }
}
