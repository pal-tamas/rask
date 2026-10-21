namespace Rask.Core.Globalization;

/// <summary>
///     Every user-visible string the framework itself renders.
/// </summary>
/// <remarks>
///     <para>
///         A closed enum rather than string keys, because the framework's own text is a <em>closed</em>
///         set: the lookup is an <c>int</c> switch with no hashing, an unknown key cannot be written,
///         and — the part that matters most in practice — this file is the single enumerable answer to
///         "what English does Rask put on a page?", which nobody could produce before.
///     </para>
///     <para>
///         The enum lives in Core and names the UI kit's concepts (<see cref="PickerClear" />,
///         <see cref="PaginationSummary" />). That is a small deliberate leak: Rask ships as one product,
///         the kit already depends on Core, and the alternative — a key registry per library — trades a closed compile-checked set for
///         stringly-typed keys in three places instead of one.
///     </para>
///     <para>
///         A text that carries values numbers them — <c>{0}</c>, <c>{1}</c> — and is read with
///         <c>RaskStrings.Get(key, english, value…)</c>, so a translation can put them in its own order.
///     </para>
/// </remarks>
public enum RaskString
{
    /// <summary>The previous-month control's accessible name in a date picker.</summary>
    PickerPreviousMonth,

    /// <summary>The next-month control's accessible name in a date picker.</summary>
    PickerNextMonth,

    /// <summary>The hour column's label in a time picker.</summary>
    PickerHour,

    /// <summary>The minute column's label in a time picker.</summary>
    PickerMinute,

    /// <summary>The seconds column's label in a time picker.</summary>
    PickerSecond,

    /// <summary>The clear (×) control's accessible name in a picker.</summary>
    PickerClear,

    /// <summary>The heading of the built-in not-found page, and the title of its tab.</summary>
    NotFoundTitle,

    /// <summary>The sentence of the built-in not-found page, with the path that was asked for written into its <c>{0}</c>.</summary>
    NotFoundBody,

    /// <summary>The link back to the home page on the built-in not-found page.</summary>
    NotFoundBackHome,

    /// <summary>The heading of the built-in error page.</summary>
    ErrorHeading,

    /// <summary>The retry control on the built-in error page.</summary>
    ErrorTryAgain,

    /// <summary>The reload control on the built-in error page.</summary>
    ErrorReload,

    /// <summary>The counted pager's summary: the first result shown, the last, and how many there are.</summary>
    PaginationSummary,

    /// <summary>The previous-page control's accessible name in a counted pager.</summary>
    PaginationPrevious,

    /// <summary>The next-page control's accessible name in a counted pager.</summary>
    PaginationNext,

    /// <summary>The accessible name of the calendar's shortcut back to today.</summary>
    CalendarToday,

    /// <summary>What a date picker says while no date is chosen.</summary>
    DatePickerPlaceholder,

    /// <summary>What a range date picker says while no range is chosen.</summary>
    DatePickerRangePlaceholder,

    /// <summary>The button that confirms a date picker's waiting choice.</summary>
    DatePickerConfirm,

    /// <summary>The button that drops a date picker's waiting choice.</summary>
    DatePickerCancel,

    /// <summary>The month part's accessible name in a typed date.</summary>
    DatePickerMonth,

    /// <summary>The day part's accessible name in a typed date.</summary>
    DatePickerDay,

    /// <summary>The year part's accessible name in a typed date.</summary>
    DatePickerYear,

    /// <summary>What the month part of a typed date shows while empty.</summary>
    DatePickerMonthPlaceholder,

    /// <summary>What the day part of a typed date shows while empty.</summary>
    DatePickerDayPlaceholder,

    /// <summary>What the year part of a typed date shows while empty.</summary>
    DatePickerYearPlaceholder,

    /// <summary>The date range preset for today.</summary>
    DateRangePresetToday,

    /// <summary>The date range preset for yesterday.</summary>
    DateRangePresetYesterday,

    /// <summary>The date range preset for this week.</summary>
    DateRangePresetThisWeek,

    /// <summary>The date range preset for last week.</summary>
    DateRangePresetLastWeek,

    /// <summary>The date range preset for the last seven days.</summary>
    DateRangePresetLast7Days,

    /// <summary>The date range preset for this month.</summary>
    DateRangePresetThisMonth,

    /// <summary>The date range preset for last month.</summary>
    DateRangePresetLastMonth,

    /// <summary>The date range preset for this quarter.</summary>
    DateRangePresetThisQuarter,

    /// <summary>The date range preset for last quarter.</summary>
    DateRangePresetLastQuarter,

    /// <summary>The date range preset for this year.</summary>
    DateRangePresetThisYear,

    /// <summary>The date range preset for last year.</summary>
    DateRangePresetLastYear,

    /// <summary>The date range preset for the last fourteen days.</summary>
    DateRangePresetLast14Days,

    /// <summary>The date range preset for the last thirty days.</summary>
    DateRangePresetLast30Days,

    /// <summary>The date range preset for the last three months.</summary>
    DateRangePresetLast3Months,

    /// <summary>The date range preset for the last six months.</summary>
    DateRangePresetLast6Months,

    /// <summary>The date range preset from the first of the year to today.</summary>
    DateRangePresetYearToDate,

    /// <summary>The date range preset for tomorrow.</summary>
    DateRangePresetTomorrow,

    /// <summary>The date range preset for next week.</summary>
    DateRangePresetNextWeek,

    /// <summary>The date range preset for the next seven days.</summary>
    DateRangePresetNext7Days,

    /// <summary>The date range preset for next month.</summary>
    DateRangePresetNextMonth,

    /// <summary>The date range preset for next quarter.</summary>
    DateRangePresetNextQuarter,

    /// <summary>The date range preset for next year.</summary>
    DateRangePresetNextYear,

    /// <summary>The date range preset for the next fourteen days.</summary>
    DateRangePresetNext14Days,

    /// <summary>The date range preset for the next thirty days.</summary>
    DateRangePresetNext30Days,

    /// <summary>The date range preset for the next three months.</summary>
    DateRangePresetNext3Months,

    /// <summary>The date range preset for the next six months.</summary>
    DateRangePresetNext6Months,

    /// <summary>The date range preset with no start.</summary>
    DateRangePresetAllTime,

    /// <summary>The date range preset for a range picked by hand.</summary>
    DateRangePresetCustom,

    /// <summary>What a select's list says while its options load.</summary>
    SelectLoading,

    /// <summary>What a select's list says when nothing matches.</summary>
    SelectEmpty,

    /// <summary>What a select's search box shows while empty.</summary>
    SelectSearchPlaceholder,

    /// <summary>The accessible name of the control that empties a select's search box.</summary>
    SelectSearchClear,

    /// <summary>The accessible name of the control that clears a select's choice.</summary>
    SelectClear,

    /// <summary>The word after the count when a select holds several choices: "3 selected".</summary>
    SelectSelectedSuffix,

    /// <summary>What a time picker says while no time is chosen.</summary>
    TimePickerPlaceholder,

    /// <summary>The accessible name of the morning-or-afternoon part of a typed time.</summary>
    TimePickerMeridiem,

    /// <summary>What the hour part of a typed time shows while empty.</summary>
    TimePickerHourPlaceholder,

    /// <summary>What the minute part of a typed time shows while empty.</summary>
    TimePickerMinutePlaceholder,

    /// <summary>The accessible name of the rich text editor's box.</summary>
    EditorLabel,

    /// <summary>The accessible name of the editor's toolbar.</summary>
    EditorToolbar,

    /// <summary>The tooltip of the editor's bold button.</summary>
    EditorBold,

    /// <summary>The tooltip of the editor's italic button.</summary>
    EditorItalic,

    /// <summary>The tooltip of the editor's strikethrough button.</summary>
    EditorStrike,

    /// <summary>The tooltip of the editor's underline button.</summary>
    EditorUnderline,

    /// <summary>The tooltip of the editor's bullet list button.</summary>
    EditorBullet,

    /// <summary>The tooltip of the editor's ordered list button.</summary>
    EditorOrdered,

    /// <summary>The tooltip of the editor's blockquote button.</summary>
    EditorBlockquote,

    /// <summary>The tooltip of the editor's code button.</summary>
    EditorCode,

    /// <summary>The tooltip of the editor's highlight button.</summary>
    EditorHighlight,

    /// <summary>The tooltip of the editor's subscript button.</summary>
    EditorSubscript,

    /// <summary>The tooltip of the editor's superscript button.</summary>
    EditorSuperscript,

    /// <summary>The tooltip of the editor's undo button.</summary>
    EditorUndo,

    /// <summary>The tooltip of the editor's redo button.</summary>
    EditorRedo,

    /// <summary>The tooltip of the editor's link button, and the name of the control that inserts the link.</summary>
    EditorLink,

    /// <summary>The accessible name of the control that removes a link in the editor.</summary>
    EditorUnlink,

    /// <summary>The tooltip of the editor's alignment select.</summary>
    EditorAlign,

    /// <summary>The editor's left alignment option.</summary>
    EditorAlignLeft,

    /// <summary>The editor's center alignment option.</summary>
    EditorAlignCenter,

    /// <summary>The editor's right alignment option.</summary>
    EditorAlignRight,

    /// <summary>The tooltip and the list name of the editor's heading select.</summary>
    EditorHeading,

    /// <summary>The editor's plain text option in the heading select.</summary>
    EditorHeadingText,

    /// <summary>The editor's first-level heading option.</summary>
    EditorHeading1,

    /// <summary>The editor's second-level heading option.</summary>
    EditorHeading2,

    /// <summary>The editor's third-level heading option.</summary>
    EditorHeading3,

    /// <summary>The accessible name of an input's clear control.</summary>
    InputClear,

    /// <summary>The accessible name of an input's copy control.</summary>
    InputCopy,

    /// <summary>The accessible name of a password input's reveal control.</summary>
    InputTogglePassword,

    /// <summary>The button of a file input that takes one file.</summary>
    InputChooseFile,

    /// <summary>The button of a file input that takes several files.</summary>
    InputChooseFiles,

    /// <summary>What a file input says while it holds no file.</summary>
    InputNoFile,

    /// <summary>The accessible name of a modal's close control.</summary>
    ModalClose,

    /// <summary>The button of the leave dialog that keeps the reader on the page.</summary>
    ConfirmLeaveStay,

    /// <summary>The button of the leave dialog that leaves the page.</summary>
    ConfirmLeaveLeave,

    /// <summary>The accessible name of one cell of a one-time code: its place, and how many cells there are.</summary>
    OtpCharacter,

    /// <summary>What a range slider's first thumb says its value is.</summary>
    SliderRangeStart,

    /// <summary>What a range slider's second thumb says its value is.</summary>
    SliderRangeEnd,

    /// <summary>The accessible name of the control that opens, closes or collapses the sidebar.</summary>
    SidebarToggle,

    /// <summary>The accessible name of the sidebar's search box when it has no placeholder.</summary>
    SidebarSearch,

    /// <summary>What a command palette says when nothing matches.</summary>
    CommandEmpty,

    /// <summary>The accessible name of the control that removes a file from an upload's list.</summary>
    FileItemRemove,

    /// <summary>What a field that must be filled says when it is left empty and its type has no words of its own.</summary>
    ValidationRequired,

    /// <summary>What a whole-number field says of text that is no whole number: a fraction, a lone minus.</summary>
    ValidationWholeNumber,

    /// <summary>What a number field says of text that is no number.</summary>
    ValidationNumber,

    /// <summary>What a number field says of a number its type cannot hold, too large or too small.</summary>
    ValidationNumberRange,

    /// <summary>What a date field says of a date half typed or one that does not exist.</summary>
    ValidationDate,

    /// <summary>What a time field says of a time half typed or one that does not exist.</summary>
    ValidationTime,

    /// <summary>What the person is told when a form's save threw: in a toast, or in the form's own messages.</summary>
    FormSaveFailed,

    /// <summary>
    ///     What the error page says in place of the exception, with the reference the log line carries
    ///     written into its <c>{0}</c>.
    /// </summary>
    ErrorReference,

    /// <summary>
    ///     What the person is told, in a toast, when a handler of theirs — a click, a change — threw and
    ///     no boundary of the app's own was there to take it.
    /// </summary>
    HandlerFailed,

    /// <summary>The title of the tab while the error page stands in for the whole app.</summary>
    ErrorTitle,
    /// <summary>The accessible name of the button that opens a color picker while no color is chosen.</summary>
    ColorPickerOpen,

    /// <summary>The accessible name of the button that opens a color picker, with the chosen color written into its <c>{0}</c>.</summary>
    ColorPickerOpenWith,

    /// <summary>The accessible name of the field a color is typed into.</summary>
    ColorPickerInput,

    /// <summary>What a color picker's field shows while empty, in every format but hex.</summary>
    ColorPickerPlaceholder,

    /// <summary>The accessible name of the area a color is dragged out of.</summary>
    ColorPickerArea,

    /// <summary>What kind of control the area of a color picker says it is.</summary>
    ColorPickerAreaRole,

    /// <summary>Where the area's thumb stands, the saturation in its <c>{0}</c> and the brightness in its <c>{1}</c>.</summary>
    ColorPickerAreaValue,

    /// <summary>The accessible name of a color picker's hue slider.</summary>
    ColorPickerHue,

    /// <summary>The accessible name of a color picker's alpha slider.</summary>
    ColorPickerOpacity,

    /// <summary>The accessible name of the field inside a color picker's popover.</summary>
    ColorPickerValue,

    /// <summary>The accessible name of a color picker's palette.</summary>
    ColorPickerSwatches,

    /// <summary>What is announced when a swatch is picked, its name in the <c>{0}</c>.</summary>
    ColorPickerSelected,

    /// <summary>The accessible name of the button that picks a color from anywhere on the screen.</summary>
    ColorPickerDropper,

    /// <summary>The accessible name of the button that empties a color picker.</summary>
    ColorPickerClear,

    /// <summary>The button that closes a color picker's sheet and keeps the color it opened with.</summary>
    ColorPickerCancel,

    /// <summary>The button that takes the color a color picker's sheet shows.</summary>
    ColorPickerSelect,

    /// <summary>What a color field says of text that is no color.</summary>
    ValidationColor,

    /// <summary>The name of the red swatch of a color picker's default palette.</summary>
    ColorSwatchRed,

    /// <summary>The name of the orange swatch of a color picker's default palette.</summary>
    ColorSwatchOrange,

    /// <summary>The name of the amber swatch of a color picker's default palette.</summary>
    ColorSwatchAmber,

    /// <summary>The name of the yellow swatch of a color picker's default palette.</summary>
    ColorSwatchYellow,

    /// <summary>The name of the green swatch of a color picker's default palette.</summary>
    ColorSwatchGreen,

    /// <summary>The name of the teal swatch of a color picker's default palette.</summary>
    ColorSwatchTeal,

    /// <summary>The name of the sky swatch of a color picker's default palette.</summary>
    ColorSwatchSky,

    /// <summary>The name of the indigo swatch of a color picker's default palette.</summary>
    ColorSwatchIndigo,

    /// <summary>The name of the purple swatch of a color picker's default palette.</summary>
    ColorSwatchPurple,

    /// <summary>The name of the pink swatch of a color picker's default palette.</summary>
    ColorSwatchPink,

    /// <summary>The name of the rose swatch of a color picker's default palette.</summary>
    ColorSwatchRose,

    /// <summary>The name of the orange light swatch of a color picker's default palette.</summary>
    ColorSwatchOrangeLight,

    /// <summary>The name of the amber light swatch of a color picker's default palette.</summary>
    ColorSwatchAmberLight,

    /// <summary>The name of the lime swatch of a color picker's default palette.</summary>
    ColorSwatchLime,

    /// <summary>The name of the emerald swatch of a color picker's default palette.</summary>
    ColorSwatchEmerald,

    /// <summary>The name of the cyan swatch of a color picker's default palette.</summary>
    ColorSwatchCyan,

    /// <summary>The name of the indigo light swatch of a color picker's default palette.</summary>
    ColorSwatchIndigoLight,

    /// <summary>The name of the violet light swatch of a color picker's default palette.</summary>
    ColorSwatchVioletLight,

    /// <summary>The name of the pink light swatch of a color picker's default palette.</summary>
    ColorSwatchPinkLight,

    /// <summary>The name of the rose light swatch of a color picker's default palette.</summary>
    ColorSwatchRoseLight,

    /// <summary>The name of the stone swatch of a color picker's default palette.</summary>
    ColorSwatchStone,

    /// <summary>The name of the slate swatch of a color picker's default palette.</summary>
    ColorSwatchSlate,

    /// <summary>The name of the black swatch of a color picker's default palette.</summary>
    ColorSwatchBlack,

    /// <summary>The name of the white swatch of a color picker's default palette.</summary>
    ColorSwatchWhite,

    /// <summary>The button that writes the range a range date picker holds: on a phone, and where it confirms.</summary>
    DatePickerConfirmRange,

    /// <summary>The option a phone's list of preset ranges shows while it names none.</summary>
    DatePickerPresetsPlaceholder,

    /// <summary>What a screen reader calls a carousel: its <c>aria-roledescription</c>.</summary>
    CarouselRole,

    /// <summary>The accessible name of a carousel.</summary>
    CarouselLabel,

    /// <summary>What a screen reader calls one slide of a carousel: its <c>aria-roledescription</c>.</summary>
    CarouselSlideRole,

    /// <summary>The accessible name of a slide: its place, and how many slides there are.</summary>
    CarouselSlideLabel,

    /// <summary>The accessible name of a carousel's previous button.</summary>
    CarouselPrevious,

    /// <summary>The accessible name of a carousel's next button.</summary>
    CarouselNext,

    /// <summary>The accessible name of a carousel's row of indicators.</summary>
    CarouselIndicators,

    /// <summary>The accessible name of one indicator of a carousel: the slide it shows.</summary>
    CarouselShowSlide,

    /// <summary>The accessible name of a phone field's country button, before the country it shows.</summary>
    PhoneChangeCountry,

    /// <summary>The reconnect overlay's message while the page tries to reach its app again.</summary>
    ConnectionReconnecting,

    /// <summary>The reconnect overlay's message while the browser reports itself offline.</summary>
    ConnectionOffline,

    /// <summary>The reconnect overlay's message once the first attempts have failed.</summary>
    ConnectionStillTrying,

    /// <summary>The reconnect overlay's button that tries to reconnect at once.</summary>
    ConnectionRetry,

    /// <summary>The reconnect overlay's button that reloads the page.</summary>
    ConnectionReload,

    /// <summary>The reconnect overlay's message while the app is being updated.</summary>
    ConnectionUpdating,

    /// <summary>The reconnect overlay's message while a sign-in or sign-out reconnects the page.</summary>
    ConnectionAuthenticating,

    /// <summary>The reconnect overlay's message once the page's session is gone.</summary>
    ConnectionSessionTimedOut,

    /// <summary>The accessible name of a command palette's button that empties its search box.</summary>
    CommandClear,

    /// <summary>The accessible name of a command palette's button that closes the modal it is in.</summary>
    CommandClose,

    /// <summary>
    ///     What the person is told, in a toast, when they acted on something their account may not do: the
    ///     handler's <c>[Authorize]</c> turned them away and nothing was changed.
    /// </summary>
    HandlerForbidden,

    /// <summary>
    ///     What a page says, inside the app's layout, to a signed-in person whose account may not open it.
    /// </summary>
    PageForbidden,

    /// <summary>The button that opens a filter set's panel, and the panel's heading.</summary>
    Filters,

    /// <summary>The filter panel's button that takes every filter off.</summary>
    FiltersClear,

    /// <summary>The button beside a filter set's badges that takes every filter off.</summary>
    FiltersClearAll,

    /// <summary>The filter panel's button that closes it.</summary>
    FiltersClose,

    /// <summary>The accessible name of a filter badge's remove button. <c>{0}</c> is the filter's label.</summary>
    FilterRemove,
}
