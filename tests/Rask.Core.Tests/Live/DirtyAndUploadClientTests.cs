using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What the browser does for a form and an upload with no round trip: marks a changed form and its fields
///     <c>data-dirty</c>, refuses a chosen file the upload would not take, previews a chosen image from the file
///     itself, and calls an upload off.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-dirty.ts</c>, <c>rask-file-checks.ts</c>, <c>rask-preview.ts</c>,
///     <c>rask-upload.ts</c> and <c>rask-upload-cancel.ts</c> in-process on Jint (<c>DirtyAndUploadFixture.ts</c>).
///     The journeys in a real browser, on both hosts, are <c>FormDirtyAndUploadTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class DirtyAndUploadClientTests
{
    private static readonly Lazy<JsonElement> Root = new(() => ScriptFixture.Run("DirtyAndUploadFixture"));

    private static JsonElement Said(string name) => Root.Value.GetProperty(name);

    private static bool[] Flags(string name) => [.. Said(name).EnumerateArray().Select(static e => e.GetBoolean())];

    [Fact]
    public void A_field_typed_into_marks_itself_and_its_form_and_typed_back_marks_neither()
    {
        var untouched = Said("untouched").GetBoolean();

        var typed = Flags("typed");
        var typedBack = Flags("typedBack");

        Assert.False(untouched);
        Assert.Equal([true, true], typed);
        Assert.Equal([false, false], typedBack);
    }

    [Fact]
    public void A_checkbox_a_select_a_radio_group_a_date_and_a_file_input_are_each_compared_by_their_own_value()
    {
        var root = Root.Value;

        var marks = (
            Checked: root.GetProperty("checkedBox").GetBoolean(),
            Unchecked: root.GetProperty("uncheckedBox").GetBoolean(),
            Picked: root.GetProperty("pickedAnother").GetBoolean(),
            PickedBack: root.GetProperty("pickedBack").GetBoolean(),
            Date: root.GetProperty("dateChanged").GetBoolean(),
            File: root.GetProperty("fileChosen").GetBoolean());

        Assert.Equal((true, false, true, false, true, true), marks);
        Assert.Equal([true, true], Flags("radioMoved"));
        Assert.Equal([false, false], Flags("radioBack"));
        Assert.True(Said("formWithFields").GetBoolean());
    }

    [Fact]
    public void A_search_box_that_holds_none_of_the_forms_data_marks_nothing()
    {
        var searching = Flags("searching");

        var anyMark = searching.Any(static mark => mark);

        Assert.False(anyMark);
    }

    [Fact]
    public void A_pick_a_drawn_control_writes_into_a_hidden_input_marks_the_form_and_not_the_input()
    {
        var hiddenPick = Flags("hiddenPick");

        var (input, form) = (hiddenPick[0], hiddenPick[1]);

        Assert.False(input);
        Assert.True(form);
    }

    [Fact]
    public void A_pick_only_the_page_heard_marks_the_form_while_the_page_says_so()
    {
        var picked = Flags("picked");

        var takenBack = Said("pickTakenBack").GetBoolean();

        Assert.Equal([true, true], picked);
        Assert.False(takenBack);
    }

    [Fact]
    public void An_accepted_save_clears_the_marks_but_a_value_typed_while_it_was_on_its_way_stays_marked()
    {
        var afterTheSave = Flags("afterTheSave");

        var typedBackToSaved = Flags("typedBackToSaved");

        Assert.Equal([false, true, true], afterTheSave);
        Assert.Equal([false, false], typedBackToSaved);
    }

    [Fact]
    public void A_refused_save_keeps_every_mark_and_a_reset_takes_them_away()
    {
        var refused = Flags("refused");

        var afterTheReset = Flags("afterTheReset");

        Assert.Equal([true, true], refused);
        Assert.Equal([false, false], afterTheReset);
    }

    [Fact]
    public void Accept_takes_an_ending_in_any_case_a_whole_type_or_an_exact_type()
    {
        var acceptance = Said("acceptance");

        bool Takes(string name) => acceptance.GetProperty(name).GetBoolean();

        Assert.True(Takes("extension"));
        Assert.False(Takes("otherExtension"));
        Assert.True(Takes("wildcard"));
        Assert.True(Takes("exactType"));
        Assert.False(Takes("exactTypeNotOther"));
        Assert.True(Takes("unknownTypeByExtension"));
        Assert.False(Takes("unknownTypeByWildcard"));
        Assert.True(Takes("empty"));
    }

    [Fact]
    public void A_file_exactly_at_the_limit_is_taken_and_nothing_is_said()
    {
        var atTheLimit = Said("atTheLimit");

        var kept = atTheLimit.GetProperty("kept").GetInt32();

        Assert.Equal(1, kept);
        Assert.Equal("", atTheLimit.GetProperty("said").GetString());
        Assert.False(atTheLimit.GetProperty("invalid").GetBoolean());
    }

    [Fact]
    public void Of_several_files_the_refused_ones_are_named_and_taken_out_and_the_rest_go_on()
    {
        var oneOfMany = Said("oneOfMany");

        var said = oneOfMany.GetProperty("said").GetString();

        Assert.Equal(1, oneOfMany.GetProperty("kept").GetInt32());
        Assert.Equal("big.png is larger than 5 MB. invoice.pdf is not a kind of file this takes.", said);
        Assert.True(oneOfMany.GetProperty("invalid")[0].GetBoolean());
        Assert.Equal("true", oneOfMany.GetProperty("invalid")[1].GetString());
    }

    [Fact]
    public void A_choice_that_passes_takes_the_sentence_and_the_red_away()
    {
        var passed = Said("passedAfter");

        var said = passed.GetProperty("said").GetString();

        Assert.Equal("", said);
        Assert.False(passed.GetProperty("invalid").GetBoolean());
    }

    [Fact]
    public void A_choice_with_nothing_left_sends_nothing_and_says_why_in_the_visitors_language()
    {
        var noneLeft = Said("noneLeft");

        var inHungarian = Said("inHungarian").GetString();

        Assert.Equal("", noneLeft.GetProperty("value").GetString());
        Assert.Equal("big.png is larger than 5 MB.", noneLeft.GetProperty("said").GetString());
        Assert.Equal("A(z) big.png túl nagy: legfeljebb 5 MB lehet.", inHungarian);
    }

    [Fact]
    public void A_preview_finds_the_chosen_image_by_its_size_and_time_and_never_a_file_that_is_no_image()
    {
        var found = Said("found");

        string? Name(string key) => found.GetProperty(key).GetString();

        Assert.Equal("cat.png", Name("byName"));
        Assert.Equal("cat.png", Name("tidiedName"));
        Assert.Equal("huge.jpg", Name("huge"));
        Assert.Equal(JsonValueKind.Null, found.GetProperty("noImage").ValueKind);
        Assert.Equal(JsonValueKind.Null, found.GetProperty("neverChosen").ValueKind);
    }

    [Fact]
    public void Each_preview_url_lives_as_long_as_its_image_and_none_is_left_behind()
    {
        var filled = Said("filled");

        var made = Said("madeAfterARender").GetInt32();
        var redrawn = Said("redrawn");
        var removed = Said("removed");

        Assert.Equal("blob:1", filled.GetProperty("photo").GetString());
        Assert.Equal(JsonValueKind.Null, filled.GetProperty("paper").ValueKind);
        Assert.Equal(1, made);
        Assert.Equal("blob:2", redrawn.GetProperty("src").GetString());
        Assert.Equal(1, redrawn.GetProperty("revoked").GetInt32());
        Assert.Equal(2, removed.GetProperty("revoked").GetInt32());
        Assert.Equal(0, removed.GetProperty("live").GetInt32());
    }

    [Fact]
    public void The_cancel_press_stops_only_its_own_upload_and_empties_its_input()
    {
        var cancelled = Said("cancelled");

        bool Flag(string name) => cancelled.GetProperty(name).GetBoolean();

        Assert.True(Flag("calledOff"));
        Assert.Equal(1, cancelled.GetProperty("aborted").GetInt32());
        Assert.Equal(0, cancelled.GetProperty("otherAborted").GetInt32());
        Assert.True(Flag("flagged"));
        Assert.True(Flag("loadingBefore"));
        Assert.False(Flag("loadingAfter"));
        Assert.True(Flag("otherStillLoading"));
        Assert.True(Flag("emptied"));
        Assert.False(Flag("again"));
    }

    [Fact]
    public void An_upload_begun_again_after_a_cancel_can_be_called_off_in_its_turn()
    {
        var reUpload = Said("reUpload");

        var aborted = reUpload.GetProperty("abortedAgain").GetInt32();

        Assert.Equal(1, aborted);
        Assert.False(reUpload.GetProperty("loading").GetBoolean());
    }
}
