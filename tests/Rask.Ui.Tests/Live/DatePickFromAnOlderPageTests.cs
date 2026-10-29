using Rask.Core;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     Days picked faster than the page answers: every pick is read from the page as loaded, and each one the page
///     hears is a render of the picker — so the last pick arrives several pages late, and is still heard.
/// </summary>
/// <remarks>
///     An event from an older page reaches the handler that is the same as the one it was sent to, among the two
///     a slot remembers (<c>Component.StaleEvents</c>). The pickers' field and clear button closed over what each
///     render resolved — the day chosen — so no two renders made the same one: the fourth pick, or a clear after
///     three, ran nothing.
/// </remarks>
public sealed partial class DatePickFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Days = ["2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04"];

    [Fact]
    public async Task A_fourth_day_picked_before_the_page_answered_the_first_three_is_written()
    {
        var model = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.DatePicker.Bind(() => model.Day).Live().Locale("en-US")));
        var picked = session.Id("input");

        var heard = await HearAllAsync(session, picked, Days);

        Assert.True(heard);
        Assert.Equal(new DateOnly(2026, 10, 4), model.Day);
    }

    [Fact]
    public async Task A_clear_pressed_after_three_picks_the_page_had_not_answered_empties_the_picker()
    {
        var model = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.DatePicker.Bind(() => model.Maybe).Live().Clearable().Locale("en-US")));
        await HearAllAsync(session, session.Id("input"), Days[..3]);

        var heard = await session.PressedAsync(session.Id("click"));

        Assert.True(heard);
        Assert.Null(model.Maybe);
    }

    [Fact]
    public async Task A_date_typed_into_its_parts_before_the_page_answered_three_others_is_written()
    {
        var model = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.DatePicker.Bind(() => model.Day).Live().Type(Ui.DatePickerType.Input).Locale("en-US")));
        var typed = session.Ids("change")[^1];

        var heard = await HearAllAsync(session, typed, Days);

        Assert.True(heard);
        Assert.Equal(new DateOnly(2026, 10, 4), model.Day);
    }

    [Fact]
    public async Task A_fourth_moment_picked_before_the_page_answered_the_first_three_is_written()
    {
        var model = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.DatetimePicker.Bind(() => model.Moment).Live().Locale("en-US")));
        var picked = session.Id("input");

        var heard = await HearAllAsync(session, picked, Days.Select(day => day + "T10:30"));

        Assert.True(heard);
        Assert.Equal(new DateTime(2026, 10, 4, 10, 30, 0), model.Moment);
    }

    [Fact]
    public async Task A_moment_cleared_after_three_picks_the_page_had_not_answered_empties_the_picker()
    {
        var model = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.DatetimePicker.Bind(() => model.MaybeMoment).Live().Clearable().Locale("en-US")));
        await HearAllAsync(session, session.Id("input"), Days[..3].Select(day => day + "T10:30"));

        var heard = await session.PressedAsync(session.Id("click"));

        Assert.True(heard);
        Assert.Null(model.MaybeMoment);
    }

    private static async Task<bool> HearAllAsync(OlderPage session, string id, IEnumerable<string> values)
    {
        var heard = true;
        foreach (var value in values)
        {
            heard = await session.TypedAsync(id, value);
        }

        return heard;
    }

    private sealed class Model
    {
        public DateOnly Day { get; set; }

        public DateOnly? Maybe { get; set; }

        public DateTime Moment { get; set; }

        public DateTime? MaybeMoment { get; set; }
    }
}
