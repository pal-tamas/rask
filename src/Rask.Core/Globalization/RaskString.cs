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

    /// <summary>The heading of the built-in not-found page.</summary>
    NotFoundTitle,

    /// <summary>The body of the built-in not-found page.</summary>
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

    /// <summary>The accessible name of a rating's empty choice.</summary>
    RatingNone,

    /// <summary>The accessible name of one star of a rating: its place, and how many stars there are.</summary>
    RatingValue,

    /// <summary>The accessible name of the control that opens, closes or collapses the sidebar.</summary>
    SidebarToggle,

    /// <summary>The accessible name of the sidebar's search box when it has no placeholder.</summary>
    SidebarSearch,

    /// <summary>What a command palette says when nothing matches.</summary>
    CommandEmpty,

    /// <summary>The data grid's column chooser, its button and its panel.</summary>
    DataGridColumns,

    /// <summary>The accessible name of the control that moves a column earlier.</summary>
    DataGridMoveUp,

    /// <summary>The accessible name of the control that moves a column later.</summary>
    DataGridMoveDown,

    /// <summary>The accessible name of the data grid's grouping panel.</summary>
    DataGridGrouping,

    /// <summary>What the data grid's grouping panel says while nothing is grouped.</summary>
    DataGridGroupingHint,

    /// <summary>The accessible name of the control that moves a grouping earlier.</summary>
    DataGridMoveGroupLeft,

    /// <summary>The accessible name of the control that moves a grouping later.</summary>
    DataGridMoveGroupRight,

    /// <summary>The accessible name of the control that stops grouping by the named column.</summary>
    DataGridUngroup,

    /// <summary>The accessible name of the control that groups by the named column.</summary>
    DataGridGroupBy,

    /// <summary>The accessible name of the control that opens a collapsed group.</summary>
    DataGridExpandGroup,

    /// <summary>The accessible name of the control that collapses a group.</summary>
    DataGridCollapseGroup,

    /// <summary>The accessible name of the column that holds the row expanders.</summary>
    DataGridExpand,

    /// <summary>The accessible name of the box that selects every row on the page.</summary>
    DataGridSelectAll,

    /// <summary>What a data grid says when it has no rows.</summary>
    DataGridEmpty,

    /// <summary>The accessible name of a row's selection box.</summary>
    DataGridSelectRow,

    /// <summary>The accessible name of the control that opens a row's detail.</summary>
    DataGridExpandRow,

    /// <summary>The accessible name of the control that closes a row's detail.</summary>
    DataGridCollapseRow,

    /// <summary>The accessible name of a diff's handle when it is given none.</summary>
    DiffHandle,

    /// <summary>The accessible name of a filter's reset choice when it is given none.</summary>
    FilterReset,

    /// <summary>What a field says while its value is being checked.</summary>
    FieldValidating,

    /// <summary>The accessible name of the control that removes a file from an upload's list.</summary>
    FileItemRemove,
}
