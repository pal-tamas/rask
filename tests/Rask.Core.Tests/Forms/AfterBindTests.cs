using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// Covers the post-bind callback wired through Input.Bound / Select.Bound / Textarea.Bound:
// `AfterBind: Action<TProp>?` and `AfterBindAsync: Func<TProp, Task>?`. Contract:
//   - Fires *after* TrySetTyped + NotifyFieldChanged, *before* field validators run.
//   - Receives the typed value the model now holds (read back via the accessor).
//   - Skipped entirely when TrySetTyped rejects the input — no stale-value leakage.
//   - Async overload is awaited before the post-handler render proceeds.
public partial class AfterBindTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_string_inputs_AfterBind_fires_on_every_keystroke_with_the_new_value()
    {
        var m = new TextModel { Name = "" };
        var observed = new List<string>();

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).AfterBind(v => observed.Add(v))
        ]);

        await page.InputAsync("{\"value\":\"A\"}");
        await page.InputAsync("{\"value\":\"Ad\"}");

        Assert.Equal(new[] { "A", "Ad" }, observed);
        Assert.Equal("Ad", m.Name);
    }

    [Fact]
    public async Task An_int_inputs_AfterBind_fires_on_change_after_the_model_is_set()
    {
        var m = new NumberModel();
        int? captured = null;

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Age).AfterBind(v => captured = v)
        ]);

        await page.ChangeAsync("{\"value\":\"42\"}");

        Assert.Equal(42, captured);
        Assert.Equal(42, m.Age);
    }

    [Fact]
    public async Task An_int_inputs_AfterBind_does_not_fire_when_the_parse_fails()
    {
        var m = new NumberModel { Age = 7 };
        var fired = false;

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Age).AfterBind(_ => fired = true)
        ]);

        await page.ChangeAsync("{\"value\":\"not-a-number\"}");

        Assert.False(fired);
        Assert.Equal(7, m.Age); // unchanged — TrySetTyped rejected it
    }

    [Fact]
    public async Task An_int_inputs_async_AfterBind_is_awaited_before_validation_runs()
    {
        // OnChange on a non-string Input both binds (setOnChange=true) and validates — this
        // is the path that lets us assert AfterBindAsync is fully awaited before validators.
        var m = new NumberModel();
        var gate = new TaskCompletionSource();
        var order = new List<string>();

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Age)
                .Validate(_ =>
                {
                    order.Add("validate");
                    return Array.Empty<string>();
                })
                .AfterBind(async _ =>
                {
                    order.Add("afterBindAsync:start");
                    await gate.Task;
                    order.Add("afterBindAsync:end");
                })
        ]);
        var changeId = page.HandlerId("change");

        // Held un-awaited: the gate below keeps the async handler mid-flight.
        var pending = page.InvokeAsync(changeId!, "{\"value\":\"42\"}");

        // Spin until the async handler reaches the gate. The dispatcher does an extra
        // Task.Yield inside InvokeWithRenderingAsync, so a single Task.Yield here may not
        // be enough.
        for (var i = 0; i < 20 && order.Count == 0; i++)
        {
            await Task.Yield();
        }

        Assert.Equal(new[] { "afterBindAsync:start" }, order);

        gate.SetResult();
        await pending;

        Assert.Equal(new[] { "afterBindAsync:start", "afterBindAsync:end", "validate" }, order);
    }

    [Fact]
    public async Task An_int_inputs_AfterBind_fires_after_the_field_change_but_before_the_validator()
    {
        var m = new NumberModel();
        var order = new List<string>();
        EditContext? captured = null;

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Age)
                .Validate(_ =>
                {
                    order.Add("validate");
                    return Array.Empty<string>();
                })
                .AfterBind(_ =>
                {
                    order.Add("afterBind");
                    // The field must already be marked modified at this point.
                    Assert.True(captured!.IsModified(new FieldIdentifier(m, nameof(NumberModel.Age))));
                }),
            Test.EditContextProbe(c => captured = c)
        ]);

        await page.ChangeAsync("{\"value\":\"9\"}");

        Assert.Equal(new[] { "afterBind", "validate" }, order);
    }

    // There used to be a sync hook and an async one, and both ran — this pinned their order. The hook is
    // one slot now, taking either shape, so there is no order to pin: what matters is that an
    // asynchronous hook is AWAITED before the render continues, which is the half that could regress
    // silently. A hook that was not awaited would still run, just too late to matter.
    [Fact]
    public async Task An_async_AfterBind_hook_is_awaited_before_the_render_continues()
    {
        var m = new TextModel { Name = "" };
        var order = new List<string>();

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name)
                .AfterBind(async _ =>
                {
                    await Task.Yield();
                    order.Add("async");
                })
        ]);

        await page.InputAsync("{\"value\":\"x\"}");

        Assert.Equal(new[] { "async" }, order);
    }

    [Fact]
    public async Task A_selects_AfterBind_fires_on_change_with_the_new_value()
    {
        var m = new ColorModel { Favorite = Color.Red };
        Color? captured = null;

        var page = Test.Render(() => Form.Model(m)[
            Select.Bind(() => m.Favorite).AfterBind(v => captured = v)[
                Option.Value(nameof(Color.Red))["Red"],
                Option.Value(nameof(Color.Blue))["Blue"]
            ]
        ]);

        await page.ChangeAsync("{\"value\":\"Blue\"}");

        Assert.Equal(Color.Blue, captured);
        Assert.Equal(Color.Blue, m.Favorite);
    }

    [Fact]
    public async Task A_selects_async_AfterBind_repopulates_a_dependent_dropdown()
    {
        // Canonical use case: when Country changes, an async lookup repopulates Cities.
        var m = new RegionModel();
        List<string>? cities = null;

        var page = Test.Render(() => Form.Model(m)[
            Select.Bind(() => m.Country).AfterBind(async c =>
            {
                await Task.Yield();
                cities = c == "US" ? new List<string> { "NYC", "LA" } : new List<string> { "Berlin" };
            })[
                Option.Value("US")["US"],
                Option.Value("DE")["DE"]
            ]
        ]);

        await page.ChangeAsync("{\"value\":\"US\"}");

        Assert.NotNull(cities);
        Assert.Equal(new[] { "NYC", "LA" }, cities!);
        Assert.Equal("US", m.Country);
    }

    [Fact]
    public async Task A_textareas_AfterBind_fires_on_input_with_the_new_value()
    {
        var m = new TextModel { Name = "" };
        string? captured = null;

        var page = Test.Render(() => Form.Model(m)[
            Textarea.Bind(() => m.Name).AfterBind(v => captured = v)
        ]);

        await page.InputAsync("{\"value\":\"hello\"}");

        Assert.Equal("hello", captured);
        Assert.Equal("hello", m.Name);
    }

    [Fact]
    public async Task A_checkboxs_AfterBind_fires_on_toggle_with_the_new_value()
    {
        var m = new FlagModel { Enabled = false };
        bool? captured = null;

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Enabled).AfterBind(v => captured = v)
        ]);
        var changeId = page.HandlerId("change");

        // Checking the box: the client reports the post-toggle checked state ("true").
        // BoolSetHandler sets the model to it and AfterBind sees the new value.
        await page.InvokeAsync(changeId!, "{\"value\":\"true\"}");

        Assert.True(captured);
        Assert.True(m.Enabled);
    }

    [Fact]
    public async Task A_string_inputs_AfterBind_still_fires_when_the_same_value_is_retyped()
    {
        // StringSetHandler always calls TrySetTyped + AfterBind for valid strings — even when
        // the user retypes the same value. This pins that observable behavior so a future
        // refactor doesn't silently introduce equality short-circuiting that would suppress
        // legitimate dependent-state recomputation.
        var m = new TextModel { Name = "x" };
        var fires = 0;

        var page = Test.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).AfterBind(_ => fires++)
        ]);

        await page.InputAsync("{\"value\":\"x\"}");
        await page.InputAsync("{\"value\":\"x\"}");

        Assert.Equal(2, fires);
    }

    private sealed class TextModel
    {
        public string Name { get; set; } = "";
    }

    private sealed class NumberModel
    {
        public int Age { get; set; }
    }

    private sealed class FlagModel
    {
        public bool Enabled { get; set; }
    }

    private sealed class ColorModel
    {
        public Color Favorite { get; set; }
    }

    private sealed class RegionModel
    {
        public string Country { get; set; } = "";
    }

    private enum Color { Red, Blue }
}
