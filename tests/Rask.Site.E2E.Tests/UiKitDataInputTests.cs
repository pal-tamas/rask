using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Data input components, in a browser.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitDataInputTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_data_input_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-text-controls", "ui-input-group", "ui-textarea", "ui-choices", "ui-range", "ui-otp", "ui-filter",
                     "ui-calendar", "ui-dates", "ui-dropzone", "ui-bound", "ui-mask",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task Typing_into_a_field_reaches_CSharp_and_comes_back() => RunAsync(async () =>
    {
        await OpenAsync();

        var email = Page.Locator("[data-testid='ui-text-controls'] input[type='email']");

        // Described by its hint while valid; the badge beside the label is not part of the field's name.
        await Expect(email).ToHaveAccessibleDescriptionAsync("For example, you@example.com.");
        await Expect(email).ToHaveAccessibleNameAsync("Email");

        await email.FillAsync("not-an-address");
        await email.BlurAsync();

        // The error only shows while the value is bad, so its appearance is the proof the value reached C#,
        // was judged there, and came back as different markup.
        var error = Page.Locator("[data-testid='ui-text-controls'] [data-ui-error]").First;
        await Expect(error).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(email).ToHaveAttributeAsync("data-invalid", "");

        // aria-describedby resolves to the VISIBLE text, error first — what a screen reader reads with the field.
        await Expect(email).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(email).ToHaveAccessibleDescriptionAsync(
            "That does not look like an email address. For example, you@example.com.");

        await email.FillAsync("ada@example.com");
        await email.BlurAsync();
        await Expect(error).ToBeHiddenAsync();
        await Expect(email).ToHaveAccessibleDescriptionAsync("For example, you@example.com.");
    });

    [Fact]
    public Task The_clear_button_appears_with_the_first_keystroke_and_empties_the_field() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-text-controls']");
        var search = scope.Locator("input[placeholder='Search orders']").Nth(2);
        var clear = scope.Locator("[data-ui-clear-button]");
        await Expect(clear).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await clear.ClickAsync();

        // Emptied in C# and patched back; hidden by CSS alone while empty, and back before any round trip.
        await Expect(search).ToHaveValueAsync("");
        await Expect(clear).ToBeHiddenAsync();
        await search.PressSequentiallyAsync("a");
        await Expect(clear).ToBeVisibleAsync();
    });

    [Fact]
    public Task The_reveal_button_shows_the_password_and_hides_it_again() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-text-controls']");
        var toggle = scope.GetByLabel("Toggle password visibility");
        var field = toggle.Locator("xpath=ancestor::*[@data-ui-input][1]").Locator("input");
        await Expect(field).ToHaveAttributeAsync("type", "password", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });

        await toggle.ClickAsync();
        await Expect(field).ToHaveAttributeAsync("type", "text");
        await toggle.ClickAsync();

        await Expect(field).ToHaveAttributeAsync("type", "password");
    });

    [Fact]
    public Task A_textarea_reports_what_was_typed_and_an_auto_one_grows() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-textarea']");
        var notes = scope.GetByLabel("Order notes");
        await notes.FillAsync("No onion.");
        await notes.BlurAsync();
        await Expect(Page.Locator("[data-testid='ui-textarea-state']")).ToContainTextAsync("No onion.");

        var auto = scope.GetByPlaceholder("This textarea will adjust to fit the content...");
        var before = (await auto.BoundingBoxAsync())!.Height;
        await auto.FillAsync("one\ntwo\nthree\nfour");

        Assert.True((await auto.BoundingBoxAsync())!.Height > before, "the auto-sizing textarea did not grow.");
    });

    [Fact]
    public Task The_one_time_code_is_a_single_field_that_takes_a_pasted_code() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-otp']");
        var field = scope.Locator("input.otp");

        // One input, not six. This is what makes a pasted or autofilled code land correctly instead of
        // dropping the whole string into the first box.
        await Expect(scope.Locator("input")).ToHaveCountAsync(1);

        // Named by its visible label and described by its hint, like every other kit field (#1117).
        await Expect(scope.GetByLabel("Verification code")).ToHaveCountAsync(1);
        await Expect(field).ToHaveAccessibleDescriptionAsync("Six digits, sent to your phone.");

        await field.FillAsync("123456");
        await field.BlurAsync();
        await Expect(Page.Locator("[data-testid='ui-otp-state']")).ToContainTextAsync("Code complete");
    });

    [Fact]
    public Task The_filter_narrows_and_resets() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-filter']");
        var state = Page.Locator("[data-testid='ui-filter-state']");

        await Expect(state).ToContainTextAsync("Showing everything");

        await scope.Locator("input[value='feature']").CheckAsync();
        await Expect(state).ToContainTextAsync("Filtered to feature");

        await scope.Locator(".filter-reset").CheckAsync();
        await Expect(state).ToContainTextAsync("Showing everything");
    });

    [Fact]
    public Task The_calendar_changes_month_and_picks_a_day() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-calendar']");
        var state = Page.Locator("[data-testid='ui-calendar-state']");

        await Expect(state).ToContainTextAsync("No date chosen");

        // Paging months is an ordinary re-render — there is no web component here to ask.
        var heading = scope.Locator(".text-sm.font-semibold").First;
        var before = await heading.TextContentAsync();
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next month" })
            .ClickAsync();
        await Expect(heading).Not.ToHaveTextAsync(before ?? "");

        await scope.Locator("table button:not([disabled])").First.ClickAsync();
        await Expect(state).ToContainTextAsync("Chosen:");
    });

    [Fact]
    public Task A_bound_control_writes_straight_to_the_model() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-bound']");
        var state = Page.Locator("[data-testid='ui-bound-state']");

        // No OnChange anywhere in this section: every one of these writes back through Bind. The
        // markup assertions in Rask.Ui.Tests can only see what a control DRAWS — this is the half that
        // proves the write-back reaches the model over a live session.
        await scope.Locator("input[type='email']").FillAsync("ada@example.com");
        await scope.Locator("input[type='email']").BlurAsync();
        await Expect(state).ToContainTextAsync("ada@example.com");

        // T comes off the model, so a bound int is a number field with nothing said at the call site.
        var seats = scope.Locator("input[type='number']");
        await Expect(seats).ToHaveCountAsync(1);
        await seats.FillAsync("4");
        await seats.BlurAsync();
        await Expect(state).ToContainTextAsync("4 seats");

        await scope.Locator("input.checkbox").CheckAsync();
        await Expect(state).ToContainTextAsync("agreed yes");

        // A rating is radios sharing a name, and a bound radio's state is `checked` rather than a
        // value attribute — which is exactly what used to be wrong.
        await scope.Locator("input.mask-star-2").Nth(2).CheckAsync();
        await Expect(state).ToContainTextAsync("3 stars");
    });

    [Fact]
    public Task The_range_reports_its_value() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-range']");

        await Expect(scope).ToContainTextAsync("Volume: 40");

        // A slider that draws a value and reports nothing is one you can push and cannot read; Ui.Range
        // had no OnChange at all before this.
        await scope.Locator("input[type='range']").FillAsync("75");
        await Expect(scope).ToContainTextAsync("Volume: 75");
    });

    [Fact]
    public Task The_drawn_select_is_a_full_keyboard_combobox() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-select']");
        var box = scope.GetByRole(AriaRole.Combobox);
        var list = scope.Locator("[role='listbox']");

        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(list).ToBeHiddenAsync();

        // Enter opens it through the button's own activation — that is the keyboard's way in, and it
        // costs no script.
        await box.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        // The roving cursor: focus stays on the box and aria-activedescendant names the option, which
        // is what keeps this free of any focus-moving JS interop.
        await Page.Keyboard.PressAsync("ArrowDown");
        var active = await box.GetAttributeAsync("aria-activedescendant");
        Assert.False(string.IsNullOrEmpty(active), "the cursor did not move");

        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("[data-testid='ui-select-state']")).ToContainTextAsync("Chosen:");
    });

    [Fact]
    public Task The_drawn_multi_select_keeps_its_list_open_across_several_picks() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-multiselect']");
        var box = scope.GetByRole(AriaRole.Combobox);
        var list = scope.Locator("[role='listbox']");
        var state = Page.Locator("[data-testid='ui-multiselect-state']");

        await box.ClickAsync();
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The whole point of the control, and the one thing a single-select cannot do: a pick does not
        // dismiss the list, so a second answer costs one click rather than another trip through the box.
        await list.GetByRole(AriaRole.Option, new LocatorGetByRoleOptions { Name = "Rask.Cli" })
            .ClickAsync();
        await Expect(list).ToBeVisibleAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        await list.GetByRole(AriaRole.Option, new LocatorGetByRoleOptions { Name = "Rask.External" })
            .ClickAsync();
        await Expect(list).ToBeVisibleAsync();
        await Expect(state).ToContainTextAsync("cli");
        await Expect(state).ToContainTextAsync("ext");

        // And the browser still owns dismissal, exactly as it does for the single-select.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(list).ToBeHiddenAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
    });

    [Fact]
    public Task A_multi_select_chip_removes_its_own_answer() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-multiselect']");
        var state = Page.Locator("[data-testid='ui-multiselect-state']");

        // The demo starts with two answers, so a chip is on screen before anything is clicked.
        await Expect(state).ToContainTextAsync("core");

        // Removing from the BOX, without opening the list at all — the affordance the chips exist for.
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove Rask.Core" })
            .ClickAsync();

        await Expect(state).Not.ToContainTextAsync("core");
        await Expect(state).ToContainTextAsync("ui");
        await Expect(scope.Locator("[role='listbox']")).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_multi_select_list_says_it_takes_more_than_one_answer() => RunAsync(async () =>
    {
        await OpenAsync();

        // Not decoration: the options carry aria-selected either way, so without this a reader has no
        // way to learn that a second one is allowed.
        await Expect(Page.Locator("[data-testid='ui-multiselect'] [role='listbox']"))
            .ToHaveAttributeAsync("aria-multiselectable", "true");
    });

    [Fact]
    public Task Escape_closes_the_drawn_select_and_CSharp_hears_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-select']");
        var box = scope.GetByRole(AriaRole.Combobox);

        await box.ClickAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        // The assertion the whole toggle event exists for. The BROWSER closes the popover here; without
        // hearing that, aria-expanded would go on claiming the list is open over a closed one.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(scope.Locator("[role='listbox']")).ToBeHiddenAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
    });

    [Fact]
    public Task Arrow_keys_in_the_drawn_select_do_not_scroll_the_page() => RunAsync(async () =>
    {
        await OpenAsync();

        var box = Page.Locator("[data-testid='ui-select']").GetByRole(AriaRole.Combobox);
        await box.ClickAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        var before = await Page.EvaluateAsync<int>("() => window.scrollY");
        for (var i = 0; i < 5; i++)
        {
            await Page.Keyboard.PressAsync("ArrowDown");
        }

        // The client never preventDefaults on its own, so without the containment added to rask-dom.ts
        // every ArrowDown would scroll the document behind the open list.
        var after = await Page.EvaluateAsync<int>("() => window.scrollY");
        Assert.Equal(before, after);
    });

    [Fact]
    public Task The_drawn_list_escapes_an_overflow_hidden_ancestor() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-select']");
        var box = scope.GetByRole(AriaRole.Combobox);
        await box.ClickAsync();

        var list = scope.Locator("[role='listbox']");
        await Expect(list).ToBeVisibleAsync();

        // The reason the popover is worth its cost: the box sits in a 96px overflow:hidden container, so
        // a list positioned inside the flow would be clipped to nothing. In the top layer it is not.
        var height = (await list.BoundingBoxAsync())!.Height;
        Assert.True(height > 96, $"the list was clipped to {height}px by its overflow-hidden ancestor.");
    });

    [Fact]
    public Task A_range_is_written_only_once_it_has_both_ends() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dates']");
        var calendar = scope.GetByRole(AriaRole.Group, new LocatorGetByRoleOptions { Name = "Stay", Exact = true });
        await calendar.ScrollIntoViewIfNeededAsync();
        var days = calendar.Locator("tbody button:not([disabled])");
        var state = Page.Locator("[data-testid='ui-dates-stay']");

        // The later day first. The first click is drawn but written nowhere: the page still has no stay.
        await days.Nth(14).ClickAsync();
        await Expect(days.Nth(14)).ToHaveAttributeAsync("aria-pressed", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(state).ToHaveTextAsync("No stay chosen.");

        await days.Nth(9).ClickAsync();
        await Expect(state).ToContainTextAsync("-10 to ", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(state).ToContainTextAsync("-15");
    });

    [Fact]
    public Task A_picker_opens_the_grid_and_closes_on_the_pick() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dates']");
        var field = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Arrival" });
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var dialog = scope.GetByRole(AriaRole.Dialog, new LocatorGetByRoleOptions { Name = "Arrival" });
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(field).ToHaveAttributeAsync("aria-expanded", "true");

        await dialog.Locator("tbody button:not([disabled])").Nth(4).ClickAsync();

        // Closed by the same click, the choice reached C#, and the field shows it.
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-testid='ui-dates-picked']")).ToContainTextAsync("Arrival: ",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(field).Not.ToContainTextAsync("Choose a date");
    });

    [Fact]
    public Task Several_days_keep_the_picker_open() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dates']");
        var field = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Days off" });
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var dialog = scope.GetByRole(AriaRole.Dialog, new LocatorGetByRoleOptions { Name = "Days off" });
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var days = dialog.Locator("tbody button:not([disabled])");

        await days.Nth(2).ClickAsync();
        await Expect(days.Nth(2)).ToHaveAttributeAsync("aria-pressed", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await days.Nth(5).ClickAsync();

        await Expect(Page.Locator("[data-testid='ui-dates-picked']")).ToContainTextAsync("2 days off",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(dialog).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_file_upload_takes_chosen_files_lists_them_and_removes_one() => RunAsync(async () =>
    {
        await OpenAsync();
        var basic = Page.Locator("[data-testid='ui-upload-basic']");
        var upload = basic.Locator("[data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();

        // A click anywhere on the area opens the picker: the area is a <label> around the input.
        var chooser = await Page.RunAndWaitForFileChooserAsync(
            () => upload.Locator("[data-ui-file-upload-dropzone]").ClickAsync());
        Assert.True(chooser.IsMultiple);
        await chooser.SetFilesAsync(new[]
        {
            new FilePayload { Name = "march.png", MimeType = "image/png", Buffer = [1, 2, 3] },
            new FilePayload { Name = "april.jpg", MimeType = "image/jpeg", Buffer = new byte[2048] },
        });

        // OnFiles handed the page both; it drew an item for each under the one that was there, with its size.
        var items = basic.Locator("[data-ui-file-item]");
        await Expect(items).ToHaveCountAsync(3, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(items.Nth(0)).ToContainTextAsync("159 KB");
        await Expect(items.Nth(0).Locator("[data-slot='image'] img")).ToBeVisibleAsync();
        await Expect(items.Nth(1)).ToContainTextAsync("march.png");
        await Expect(items.Nth(1)).ToContainTextAsync("3 B");
        await Expect(items.Nth(2)).ToContainTextAsync("2 KB");

        await basic.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove file: march.png" })
            .ClickAsync();
        await Expect(items).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(items.Nth(1)).ToContainTextAsync("april.jpg");
    });

    [Fact]
    public Task A_file_upload_is_reached_by_keyboard_and_rings_its_dropzone() => RunAsync(async () =>
    {
        await OpenAsync();
        var upload = Page.Locator("[data-testid='ui-upload-inline'] [data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();

        // The real input holds the focus, out of sight; the ring is drawn on the dropzone beside it.
        await upload.Locator("input[type=file]").FocusAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Page.Keyboard.PressAsync("Tab");
        await Expect(upload.Locator("input[type=file]")).ToBeFocusedAsync();
        var outline = await upload.Locator("[data-ui-file-upload-dropzone]")
            .EvaluateAsync<string>("z => getComputedStyle(z).outlineStyle");
        Assert.Equal("auto", outline);

        var chooser = await Page.RunAndWaitForFileChooserAsync(() => Page.Keyboard.PressAsync("Space"));
        Assert.True(chooser.IsMultiple);
    });

    [Fact]
    public Task A_file_dragged_over_an_upload_marks_it_and_puts_the_input_under_the_pointer() => RunAsync(async () =>
    {
        await OpenAsync();
        var upload = Page.Locator("[data-testid='ui-upload-inline'] [data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();

        // At rest what is under the pointer is the dropzone; the input is a pixel, out of sight.
        Assert.NotEqual("INPUT", await UnderTheMiddleOf(upload));

        // The runtime marks the area for a drag carrying FILES, and the input is then laid over all of it —
        // so the browser's own drop puts the files in it, and no script routes them.
        await upload.EvaluateAsync(
            @"zone => { const files = new DataTransfer(); files.items.add(new File(['x'], 'x.pdf'));
                        zone.querySelector('[data-ui-file-upload-dropzone]')
                            .dispatchEvent(new DragEvent('dragenter', {bubbles: true, dataTransfer: files})); }");
        await Expect(upload).ToHaveAttributeAsync("data-dragging", "");
        Assert.Equal("INPUT", await UnderTheMiddleOf(upload));

        await upload.EvaluateAsync(
            @"zone => { const files = new DataTransfer(); files.items.add(new File(['x'], 'x.pdf'));
                        zone.querySelector('[data-ui-file-upload-dropzone]')
                            .dispatchEvent(new DragEvent('dragleave', {bubbles: true, dataTransfer: files})); }");
        Assert.Null(await upload.GetAttributeAsync("data-dragging"));

        // A row of a sortable list dragged across is not an offer to upload it.
        await upload.EvaluateAsync(
            @"zone => { const text = new DataTransfer(); text.setData('text/plain', 'row');
                        zone.dispatchEvent(new DragEvent('dragenter', {bubbles: true, dataTransfer: text})); }");
        Assert.Null(await upload.GetAttributeAsync("data-dragging"));
    });

    [Fact]
    public Task A_dropped_file_reaches_the_page_like_a_chosen_one() => RunAsync(async () =>
    {
        await OpenAsync();
        var inline = Page.Locator("[data-testid='ui-upload-inline']");
        var upload = inline.Locator("[data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();
        var path = Path.Combine(Path.GetTempPath(), $"rask-drop-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "hello");

        try
        {
            // The browser's own drag pipeline, not a synthetic event: only that fills an input on a drop.
            var box = (await upload.BoundingBoxAsync())!;
            var (edgeX, edgeY) = (box.X + 6, box.Y + 6);
            var (midX, midY) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
            var cdp = await Page.Context.NewCDPSessionAsync(Page);
            foreach (var (type, x, y) in new[]
                     {
                         ("dragEnter", edgeX, edgeY), ("dragOver", edgeX, edgeY), ("dragOver", midX, midY),
                         ("drop", midX, midY),
                     })
            {
                await cdp.SendAsync("Input.dispatchDragEvent", new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["x"] = x,
                    ["y"] = y,
                    ["data"] = new Dictionary<string, object>
                    {
                        ["items"] = Array.Empty<object>(),
                        ["files"] = new[] { path },
                        ["dragOperationsMask"] = 1,
                    },
                });
            }

            await Expect(inline.Locator("[data-ui-file-item]")).ToContainTextAsync(
                Path.GetFileName(path), new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
            Assert.Null(await upload.GetAttributeAsync("data-dragging"));
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public Task A_disabled_upload_opens_nothing_and_a_custom_one_takes_a_file_all_the_same() => RunAsync(async () =>
    {
        await OpenAsync();
        var disabled = Page.Locator("[data-testid='ui-upload-disabled'] [data-ui-file-upload]");
        await disabled.ScrollIntoViewIfNeededAsync();

        await Expect(disabled.Locator("input[type=file]")).ToBeDisabledAsync();
        Assert.Equal("none", await disabled.Locator("[data-ui-file-upload-dropzone]")
            .EvaluateAsync<string>("z => getComputedStyle(z).pointerEvents"));

        // Markup of the page's own in place of the dropzone: the same input behind it.
        var custom = Page.Locator("[data-testid='ui-upload-custom']");
        await custom.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = "me.png", MimeType = "image/png", Buffer = [1, 2, 3] });
        await Expect(custom).ToContainTextAsync(
            "Chosen: me.png", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });

    private static Task<string> UnderTheMiddleOf(ILocator area) =>
        area.EvaluateAsync<string>(
            @"a => { const r = a.getBoundingClientRect();
                     return document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2).tagName; }");

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
