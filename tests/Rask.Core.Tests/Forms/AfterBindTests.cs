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
public class AfterBindTests
{
    [Fact]
    public async Task Input_String_AfterBind_FiresOnEveryKeystroke_WithNewValue()
    {
        var m = new TextModel { Name = "" };
        var observed = new List<string>();

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Name, v => observed.Add(v))
        ]);
        var html = view.RenderAsLiveRoot();
        var inputId = Markup.Attr(html, "data-rask-on-input")!;

        using var k1 = JsonDocument.Parse("{\"value\":\"A\"}");
        await view.TryInvokeHandlerAsync(inputId, k1.RootElement);
        using var k2 = JsonDocument.Parse("{\"value\":\"Ad\"}");
        await view.TryInvokeHandlerAsync(inputId, k2.RootElement);

        Assert.Equal(new[] { "A", "Ad" }, observed);
        Assert.Equal("Ad", m.Name);
    }

    [Fact]
    public async Task Input_Int_AfterBind_FiresOnChange_AfterModelIsSet()
    {
        var m = new NumberModel();
        int? captured = null;

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Age, v => captured = v)
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = Markup.Attr(html, "data-rask-on-change")!;

        using var change = JsonDocument.Parse("{\"value\":\"42\"}");
        await view.TryInvokeHandlerAsync(changeId, change.RootElement);

        Assert.Equal(42, captured);
        Assert.Equal(42, m.Age);
    }

    [Fact]
    public async Task Input_Int_AfterBind_DoesNotFire_WhenParseFails()
    {
        var m = new NumberModel { Age = 7 };
        var fired = false;

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Age, _ => fired = true)
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = Markup.Attr(html, "data-rask-on-change")!;

        using var change = JsonDocument.Parse("{\"value\":\"not-a-number\"}");
        await view.TryInvokeHandlerAsync(changeId, change.RootElement);

        Assert.False(fired);
        Assert.Equal(7, m.Age); // unchanged — TrySetTyped rejected it
    }

    [Fact]
    public async Task Input_Int_AfterBindAsync_IsAwaited_BeforeValidationRuns()
    {
        // OnChange on a non-string Input both binds (setOnChange=true) and validates — this
        // is the path that lets us assert AfterBindAsync is fully awaited before validators.
        var m = new NumberModel();
        var gate = new TaskCompletionSource();
        var order = new List<string>();

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Age,
                _ =>
                {
                    order.Add("validate");
                    return Array.Empty<string>();
                },
                AfterBindAsync: async _ =>
                {
                    order.Add("afterBindAsync:start");
                    await gate.Task;
                    order.Add("afterBindAsync:end");
                })
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = Markup.Attr(html, "data-rask-on-change")!;

        using var change = JsonDocument.Parse("{\"value\":\"42\"}");
        var pending = view.TryInvokeHandlerAsync(changeId, change.RootElement);

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
    public async Task Input_Int_AfterBind_FiresAfterNotifyFieldChanged_ButBeforeValidator()
    {
        var m = new NumberModel();
        var order = new List<string>();
        EditContext? captured = null;

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Age,
                _ =>
                {
                    order.Add("validate");
                    return Array.Empty<string>();
                },
                _ =>
                {
                    order.Add("afterBind");
                    // The field must already be marked modified at this point.
                    Assert.True(captured!.IsModified(new FieldIdentifier(m, nameof(NumberModel.Age))));
                }),
            new ContextCapture(c => captured = c)
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = Markup.Attr(html, "data-rask-on-change")!;

        using var change = JsonDocument.Parse("{\"value\":\"9\"}");
        await view.TryInvokeHandlerAsync(changeId, change.RootElement);

        Assert.Equal(new[] { "afterBind", "validate" }, order);
    }

    [Fact]
    public async Task Input_BothSyncAndAsync_RunSyncFirst_ThenAwaitAsync()
    {
        var m = new TextModel { Name = "" };
        var order = new List<string>();

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Name,
                _ => order.Add("sync"),
                async _ =>
                {
                    await Task.Yield();
                    order.Add("async");
                })
        ]);
        var html = view.RenderAsLiveRoot();
        var inputId = Markup.Attr(html, "data-rask-on-input")!;

        using var k = JsonDocument.Parse("{\"value\":\"x\"}");
        await view.TryInvokeHandlerAsync(inputId, k.RootElement);

        Assert.Equal(new[] { "sync", "async" }, order);
    }

    [Fact]
    public async Task Select_AfterBind_FiresOnChange_WithNewValue()
    {
        var m = new ColorModel { Favorite = Color.Red };
        Color? captured = null;

        var view = new StubComponent(() => Form(m)[
            Select(() => m.Favorite, v => captured = v)[
                Option(nameof(Color.Red))["Red"],
                Option(nameof(Color.Blue))["Blue"]
            ]
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = Markup.Attr(html, "data-rask-on-change")!;

        using var change = JsonDocument.Parse("{\"value\":\"Blue\"}");
        await view.TryInvokeHandlerAsync(changeId, change.RootElement);

        Assert.Equal(Color.Blue, captured);
        Assert.Equal(Color.Blue, m.Favorite);
    }

    [Fact]
    public async Task Select_AfterBindAsync_DependentDropdownScenario()
    {
        // Canonical use case: when Country changes, an async lookup repopulates Cities.
        var m = new RegionModel();
        List<string>? cities = null;

        var view = new StubComponent(() => Form(m)[
            Select(() => m.Country, AfterBindAsync: async c =>
            {
                await Task.Yield();
                cities = c == "US" ? new List<string> { "NYC", "LA" } : new List<string> { "Berlin" };
            })[
                Option("US")["US"],
                Option("DE")["DE"]
            ]
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = Markup.Attr(html, "data-rask-on-change")!;

        using var pick = JsonDocument.Parse("{\"value\":\"US\"}");
        await view.TryInvokeHandlerAsync(changeId, pick.RootElement);

        Assert.NotNull(cities);
        Assert.Equal(new[] { "NYC", "LA" }, cities!);
        Assert.Equal("US", m.Country);
    }

    [Fact]
    public async Task Textarea_AfterBind_FiresOnInput_WithNewValue()
    {
        var m = new TextModel { Name = "" };
        string? captured = null;

        var view = new StubComponent(() => Form(m)[
            Textarea(() => m.Name, v => captured = v)
        ]);
        var html = view.RenderAsLiveRoot();
        var inputId = Markup.Attr(html, "data-rask-on-input")!;

        using var k = JsonDocument.Parse("{\"value\":\"hello\"}");
        await view.TryInvokeHandlerAsync(inputId, k.RootElement);

        Assert.Equal("hello", captured);
        Assert.Equal("hello", m.Name);
    }

    [Fact]
    public async Task Checkbox_AfterBind_FiresOnToggle_WithNewValue()
    {
        var m = new FlagModel { Enabled = false };
        bool? captured = null;

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Enabled, v => captured = v)
        ]);
        var html = view.RenderAsLiveRoot();
        var changeId = Markup.Attr(html, "data-rask-on-change")!;

        // Checking the box: the client reports the post-toggle checked state ("true").
        // BoolSetHandler sets the model to it and AfterBind sees the new value.
        using var click = JsonDocument.Parse("{\"value\":\"true\"}");
        await view.TryInvokeHandlerAsync(changeId, click.RootElement);

        Assert.True(captured);
        Assert.True(m.Enabled);
    }

    [Fact]
    public async Task Input_String_AfterBind_NotFired_WhenNoValueChange_DoesNotApply()
    {
        // StringSetHandler always calls TrySetTyped + AfterBind for valid strings — even when
        // the user retypes the same value. This pins that observable behavior so a future
        // refactor doesn't silently introduce equality short-circuiting that would suppress
        // legitimate dependent-state recomputation.
        var m = new TextModel { Name = "x" };
        var fires = 0;

        var view = new StubComponent(() => Form(m)[
            Input(() => m.Name, _ => fires++)
        ]);
        var html = view.RenderAsLiveRoot();
        var inputId = Markup.Attr(html, "data-rask-on-input")!;

        using var same = JsonDocument.Parse("{\"value\":\"x\"}");
        await view.TryInvokeHandlerAsync(inputId, same.RootElement);
        await view.TryInvokeHandlerAsync(inputId, same.RootElement);

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
