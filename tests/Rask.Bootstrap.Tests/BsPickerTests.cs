using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Globalization;

namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for the custom-popover pickers (BsDatePicker / BsTimePicker /
// BsDateTimePicker). The calendar is localized, so markup is asserted under a fixed culture. The
// popover is always in the DOM (toggled by .show like BsMultiSelect), so its grid/time markup is
// present in ToHtml(). The click→writeback path needs the live runtime and is covered by the
// showcase E2E journey (same split as BsMultiSelect, whose interaction is E2E-only).
public partial class BsPickerTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Jul7 = new(2026, 7, 7);
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    // `new`: hides the <html> tag entry the markup host brings in (CS0108). This helper is what the file
    // means by Html, and the tag is never named here.
    private static new string Html(CultureInfo culture, Func<Component> render)
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
        var html = Html(Us, () => BsDatePicker.Value(Jul7).Label("Day").Id("d"));
        Assert.Contains("<label class=\"form-label\" for=\"d\">Day</label>", html);
        Assert.Contains("role=\"combobox\"", html);
        Assert.Contains("aria-haspopup=\"grid\"", html);
        Assert.Contains("aria-controls=\"d-cal\"", html);
        Assert.Contains("type=\"text\" value=\"7/7/2026\"", html);
        Assert.Contains("bs-picker-caret", html);
    }

    [Fact]
    public void Date_OptsIntoTheFixedPopoverHelper()
    {
        // The .dropdown wrapper is marked data-rask-popover and the combobox box data-rask-anchor, so the
        // runtime re-anchors the open popover with position:fixed (escapes overflow-clipping ancestors).
        var html = Html(Us, () => BsDatePicker.Value(Jul7).Id("d"));
        Assert.Contains("class=\"dropdown position-relative\" data-rask-popover=\"\"", html);
        Assert.Contains("data-rask-anchor=\"\" role=\"combobox\"", html);
    }

    [Fact]
    public void Date_Grid_HasGridRolesSelectedDayAndSevenHeaders()
    {
        var html = Html(Us, () => BsDatePicker.Value(Jul7).Id("d"));
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
            Html(Us, () => BsDatePicker.Value(Jul7)));

    [Fact]
    public void Date_DeDe_IsMondayFirstAndLocalizedMonth()
    {
        var html = Html(De, () => BsDatePicker.Value(Jul7));
        Assert.Contains("role=\"columnheader\" aria-label=\"Montag\">Mo<", html);
        Assert.Contains("<span class=\"fw-semibold\">Juli 2026</span>", html);
    }

    [Fact]
    public void Date_MinMax_GreysOutOfRangeDaysAndDisablesPrev()
    {
        var html = Html(Us, () =>
            BsDatePicker.Value(new DateOnly(2026, 7, 15)).Min(new DateOnly(2026, 7, 10)).Id("d"));
        // A day before Min is disabled + aria-disabled.
        Assert.Contains("id=\"d-d-20260705\" class=\"bs-cal-cell disabled\"", html);
        Assert.Contains("aria-label=\"Sunday, July 5, 2026\" aria-disabled=\"true\"", html);
        // The whole previous month is below Min, so the prev button is disabled.
        Assert.Contains("aria-label=\"Previous month\" type=\"button\" disabled", html);
    }

    [Fact]
    public void Date_NullableEmpty_ShowsPlaceholderAndNoClear()
    {
        var html = Html(Us, () => BsDatePicker.Value<DateOnly?>(null).Placeholder("pick"));
        Assert.Contains("value=\"\" placeholder=\"pick\"", html);
        Assert.DoesNotContain("btn-close", html);
    }

    [Fact]
    public void Date_NullableWithValue_ShowsClearButton()
    {
        var html = Html(Us, () => BsDatePicker.Value<DateOnly?>(Jul7));
        Assert.Contains("btn-close", html);
        // Closed picker's × has no raised z-index (.bs-clear-open) — see the BsSelect equivalent.
        Assert.DoesNotContain("bs-clear-open", html);
    }

    [Fact]
    public void Date_NonFloating_WrapsBoxAndCaretInPositionRelative()
    {
        // The caret/× are absolutely placed; a position-relative wrapper around the box (inside the .dropdown)
        // anchors them to the box alone. Without it they'd centre over the label-above + box stack and land on
        // the box's top edge instead of vertically centred in it.
        var html = Html(Us, () => BsDatePicker.Value(Jul7).Label("Day").Id("d"));
        Assert.Contains("<div class=\"position-relative\"><input", html);
        Assert.Contains("bs-picker-caret", html);
    }

    [Fact]
    public void Date_NonNullableWithValue_HasNoClearButton() =>
        Assert.DoesNotContain("btn-close", Html(Us, () => BsDatePicker.Value(Jul7)));

    [Fact]
    public void Date_Native_RendersNativeInputNotGrid()
    {
        var html = Html(Us, () => BsDatePicker.Value(Jul7).Native(true));
        Assert.Contains("type=\"date\"", html);
        Assert.Contains("value=\"2026-07-07\"", html);
        Assert.DoesNotContain("role=\"grid\"", html);
    }

    [Fact]
    public void Date_Disabled_IsNotFocusable()
    {
        var html = Html(Us, () => BsDatePicker.Value(Jul7).Disabled(true));
        Assert.Contains("disabled autocomplete=\"off\"", html);
        Assert.DoesNotContain("tabindex=\"0\"", html);
    }

    [Fact]
    public void Date_Bound_RendersModelValueAndPropertyDerivedIds()
    {
        var model = new DayModel { Day = Jul7 };
        var html = Html(Us, () => BsDatePicker.Bind(() => model.Day));
        Assert.Contains("value=\"7/7/2026\"", html);
        Assert.Contains("aria-controls=\"Day-cal\"", html); // controlId derives from the property name
        Assert.Contains("id=\"Day-d-20260707\"", html);
    }

    [Fact]
    public void Date_TwoIdlessPickers_GetUniqueGridIds()
    {
        // Two controlled pickers without an Id must not collide on grid/cell ids (else aria-controls /
        // aria-activedescendant resolve to the wrong calendar).
        var html = Html(Us, () => Div[
            BsDatePicker.Value(Jul7),
            BsDatePicker.Value(Jul7)
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
        var html = Html(Us, () => BsTimePicker.Value(new TimeOnly(9, 30)).MinuteStep(15));
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
            Html(Us, () => BsTimePicker.Value(new TimeOnly(9, 30)).Native(true)));

    [Fact]
    public void Time_MinMax_GreysOutOfRangeHoursAndMinutes()
    {
        var html = Html(Us, () => BsTimePicker
            .Value(new TimeOnly(9, 45))
            .Min(new TimeOnly(9, 30))
            .Max(new TimeOnly(17, 0))
            .MinuteStep(15));
        // Hours outside [9,17] are greyed + non-clickable (the padded ">08"/">18" content pins the hour item).
        Assert.Contains("bs-time-item disabled\" data-rask-key=\"8\" aria-disabled=\"true\" type=\"button\">08", html);
        Assert.Contains("bs-time-item disabled\" data-rask-key=\"18\" aria-disabled=\"true\" type=\"button\">18", html);
        // On the Min hour, minutes before Min's minute are greyed — hour 15 stays enabled, so the disabled
        // data-rask-key="15" uniquely pins the minute item.
        Assert.Contains("bs-time-item disabled\" data-rask-key=\"15\" aria-disabled=\"true\" type=\"button\">15", html);
        // The in-range selected hour is active, not disabled.
        Assert.Contains("bs-time-item active\" data-rask-key=\"9\" aria-selected=\"true\" type=\"button\">09", html);
    }

    [Fact]
    public void Time_MinMax_GreysBoundaryHourWithNoSelectableStep()
    {
        // Min 10:59 with 5-min steps renders only …:55 in hour 10 — no stepped minute reaches 59, so hour 10
        // itself is greyed (else a click would compose 10:00 and Clamp would silently snap it to 10:59).
        var html = Html(Us, () => BsTimePicker.Value<TimeOnly?>(null).Min(new TimeOnly(10, 59)).MinuteStep(5));
        Assert.Contains("bs-time-item disabled\" data-rask-key=\"10\" aria-disabled=\"true\" type=\"button\">10", html);
        Assert.Contains("bs-time-item disabled\" data-rask-key=\"9\" aria-disabled=\"true\" type=\"button\">09", html);
        // 11:00 ≥ 10:59, so hour 11 stays selectable.
        Assert.DoesNotContain("bs-time-item disabled\" data-rask-key=\"11\"", html);
    }

    [Fact]
    public void Time_Seconds_RendersThirdColumnAndStep()
    {
        var html = Html(Us, () => BsTimePicker
            .Value(new TimeOnly(9, 30, 45))
            .Seconds(true)
            .SecondStep(15)
            .MinuteStep(15));
        Assert.Contains("role=\"listbox\" aria-label=\"Second\"", html);
        // Two separators now (H:M:S), and the selected second is active (minute 45 is not, so this pins it).
        Assert.Equal(2, CountOccurrences(html, "bs-time-sep"));
        Assert.Contains("bs-time-item active\" data-rask-key=\"45\" aria-selected=\"true\" type=\"button\">45", html);
    }

    // ---- DateTime ---------------------------------------------------------------------------------

    [Fact]
    public void DateTime_Controlled_RendersCalendarAndTime()
    {
        var html = Html(Us, () => BsDateTimePicker.Value(new DateTime(2026, 7, 7, 9, 30, 0)));
        Assert.Contains("class=\"bs-datetime", html);
        Assert.Contains("role=\"grid\"", html);
        Assert.Contains("role=\"listbox\" aria-label=\"Hour\"", html);
        Assert.Contains("aria-selected=\"true\" type=\"button\">30", html);
    }

    [Fact]
    public void DateTime_Native_RendersNativeInput() =>
        Assert.Contains("type=\"datetime-local\"",
            Html(Us, () => BsDateTimePicker.Value(new DateTime(2026, 7, 7, 9, 30, 0)).Native(true)));

    [Fact]
    public void DateTime_MinMax_BoundaryDayGreysTimeItems()
    {
        var html = Html(Us, () => BsDateTimePicker
            .Value(new DateTime(2026, 7, 10, 12, 0, 0))
            .Min(new DateTime(2026, 7, 10, 9, 0, 0))
            .Max(new DateTime(2026, 7, 10, 17, 0, 0)));
        // On the boundary day the time column floors at 09:00 and caps at 17:00. The zero-padded button
        // content (">08"/">18") pins the time item — calendar cells are <div>s showing unpadded day numbers.
        Assert.Contains("bs-time-item disabled\" data-rask-key=\"8\" aria-disabled=\"true\" type=\"button\">08", html);
        Assert.Contains("bs-time-item disabled\" data-rask-key=\"18\" aria-disabled=\"true\" type=\"button\">18", html);
        Assert.Contains("bs-time-item active\" data-rask-key=\"12\" aria-selected=\"true\" type=\"button\">12", html);
    }

    [Fact]
    public void DateTime_Seconds_RendersSecondsColumn()
    {
        var html = Html(Us, () => BsDateTimePicker
            .Value(new DateTime(2026, 7, 7, 9, 30, 45))
            .Seconds(true)
            .SecondStep(15));
        Assert.Contains("role=\"listbox\" aria-label=\"Second\"", html);
        // The selected second is active (minute is 30, so an active data-rask-key="45" pins the second item).
        Assert.Contains("bs-time-item active\" data-rask-key=\"45\" aria-selected=\"true\" type=\"button\">45", html);
    }

    // ---- Chrome text ------------------------------------------------------------------------------
    //
    // The nav/column/clear accessible names used to be a per-INSTANCE record (BsPickerLabels) that every
    // call site threaded through, so translating three pickers on one page meant repeating yourself
    // three times. They now come from RaskStrings, which reads the visitor's language — an app supplies
    // Resources/RaskStrings.{culture}.json once and every picker follows.

    [Fact]
    public void Chrome_text_defaults_to_the_frameworks_english()
    {
        var html = Html(Us, () => BsDatePicker.Value<DateOnly?>(Jul7));

        Assert.Contains("aria-label=\"Previous month\"", html);
        Assert.Contains("aria-label=\"Next month\"", html);
    }

    [Fact]
    public void Chrome_text_follows_a_registered_translation()
    {
        // ASCII on purpose: the serializer HTML-encodes non-ASCII, so a real Hungarian string arrives in
        // the markup as numeric entities. That encoding is asserted on its own below; here the point is
        // only that the app's text replaces the framework's.
        using var _ = new FrameworkStrings(new Dictionary<RaskString, string>
        {
            [RaskString.PickerPreviousMonth] = "Elozo honap",
            [RaskString.PickerNextMonth] = "Kovetkezo honap",
        });

        var html = Html(Us, () => BsDatePicker.Value<DateOnly?>(Jul7));

        Assert.Contains("aria-label=\"Elozo honap\"", html);
        Assert.Contains("aria-label=\"Kovetkezo honap\"", html);
    }

    [Fact]
    public void Translated_text_is_HTML_encoded_like_any_other_text()
    {
        // Worth pinning: translations are ordinary text, so they go through the same encoder as
        // everything else. Accented characters reach the markup as numeric entities rather than raw
        // bytes — which is correct, and is why the tests around this one use ASCII.
        using var _ = new FrameworkStrings(new Dictionary<RaskString, string>
        {
            [RaskString.PickerPreviousMonth] = "Előző hónap",
        });

        var html = Html(Us, () => BsDatePicker.Value<DateOnly?>(Jul7));

        Assert.DoesNotContain("Előző", html, StringComparison.Ordinal);
        Assert.Contains("&#x151;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_the_app_did_not_translate_keeps_the_frameworks_english()
    {
        // A partial translation must not blank the rest of the chrome.
        using var _ = new FrameworkStrings(new Dictionary<RaskString, string>
        {
            [RaskString.PickerPreviousMonth] = "Elozo honap",
        });

        var html = Html(Us, () => BsDatePicker.Value<DateOnly?>(Jul7));

        Assert.Contains("aria-label=\"Elozo honap\"", html);
        Assert.Contains("aria-label=\"Next month\"", html);
    }

    [Fact]
    public void Time_column_headings_follow_a_registered_translation()
    {
        using var _ = new FrameworkStrings(new Dictionary<RaskString, string>
        {
            [RaskString.PickerHour] = "Ora",
            [RaskString.PickerMinute] = "Perc",
            [RaskString.PickerSecond] = "Masodperc",
        });

        var html = Html(Us, () => BsTimePicker.Value(new TimeOnly(9, 30, 0)).Seconds(true));

        Assert.Contains("aria-label=\"Ora\"", html);
        Assert.Contains("aria-label=\"Perc\"", html);
        Assert.Contains("aria-label=\"Masodperc\"", html);
    }

    // Registers a translation for the duration of a test, and takes it away afterwards — the source is
    // process-wide, so a leak would silently retranslate every later test in the run.
    private sealed class FrameworkStrings : IRaskStringSource, IDisposable
    {
        private readonly Dictionary<RaskString, string> _text;

        public FrameworkStrings(Dictionary<RaskString, string> text)
        {
            _text = text;
            RaskStrings.UseSource(this);
        }

        public string? Get(RaskString key, string cultureTag) =>
            _text.TryGetValue(key, out var value) ? value : null;

        public void Dispose() => RaskStrings.ResetForTests();
    }

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
