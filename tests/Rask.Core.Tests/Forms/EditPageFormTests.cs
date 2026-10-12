using Rask.Core.Forms;
using Rask.Core.Live;
using Entries = RaskEntriesRask_Core_Tests;

#pragma warning disable RASK014 // the test owns the roots it renders; there is no parent to build them

namespace Rask.Core.Tests.Forms;

// An edit page reads its record in a hook and draws the form once the record is there. Two things have to
// hold for that to be the whole recipe: the page ends on the form however the read was awaited, and a form
// component that is handed the model as a prop follows the model it is handed — a second instance under the
// same Key is a second form, with its own EditContext and nothing left of what was typed into the first.
public partial class EditPageFormTests : global::Rask.Core.RaskMarkup
{
    private const string NoSuch = "no such trailer";

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 30)]
    [InlineData(true, 0)]
    [InlineData(true, 30)]
    public async Task A_first_render_of_an_edit_page_waits_for_its_read_and_is_the_form_with_the_record(bool inOnUpdated, int delayMs)
    {
        var page = new TrailerEditPage { ReadInOnUpdated = inOnUpdated, Read = () => TrailerAfter(delayMs) };

        var result = await RaskPrerender.RenderDocument(
            page, RenderHarness.EmptyServices(), WaitFor.HangCeiling, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut);
        Assert.Contains("value=\"Trailer 7\"", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(NoSuch, result.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_edit_page_drawn_before_its_read_is_back_asks_to_be_drawn_again_and_is_then_the_form(bool inOnUpdated)
    {
        var read = new TaskCompletionSource<Trailer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new RecordingHandle();
        var page = new TrailerEditPage { ReadInOnUpdated = inOnUpdated, Read = () => read.Task, RenderHandle = handle };
        var services = RenderHarness.EmptyServices();
        var before = page.RenderAsLiveRoot(services);

        read.SetResult(new Trailer { Name = "Trailer 7", Axles = 2 });
        await WaitFor.True(() => Volatile.Read(ref handle.RequestRenderCount) > 0, "the page to ask for a render once its read is back");
        var after = page.RenderAsLiveRoot(services);

        Assert.Contains(NoSuch, before, StringComparison.Ordinal);
        Assert.Contains("value=\"Trailer 7\"", after, StringComparison.Ordinal);
        Assert.DoesNotContain(NoSuch, after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_component_that_reads_its_model_in_Render_shows_the_model_it_is_handed_later()
    {
        var host = new TrailerFormHost();
        var page = Page.Render(host);
        var empty = page.Html;

        host.Show(new Trailer { Name = "Trailer 7", Axles = 2 });
        page.Render();

        Assert.Contains(NoSuch, empty, StringComparison.Ordinal);
        Assert.Equal("Trailer 7", page.Find("#name").Attribute("value"));
        Assert.DoesNotContain(NoSuch, page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_component_handed_another_model_under_the_same_key_is_that_models_form_with_nothing_typed_left_over()
    {
        var first = new Trailer { Name = "Trailer 7", Axles = 2 };
        var second = new Trailer { Name = "Trailer 8", Axles = 3 };
        var host = new TrailerFormHost();
        var page = Page.Render(host);
        host.Show(first);
        page.Render();
        await page.On("#name").Change("typed over seven");
        await page.On("#axles").Change("two and a half");
        var contextOfTheFirst = host.Context;
        var unreadBefore = contextOfTheFirst!.GetValidationMessages().ToArray();

        host.Show(second);
        page.Render();

        Assert.NotEmpty(unreadBefore);
        Assert.Equal("Trailer 8", page.Find("#name").Attribute("value"));
        Assert.Equal("3", page.Find("#axles").Attribute("value"));
        Assert.NotSame(contextOfTheFirst, host.Context);
        Assert.Same(second, host.Context!.Model);
        Assert.Empty(host.Context.GetValidationMessages());
        Assert.Equal(1, host.FormsMounted);
        Assert.Equal("typed over seven", first.Name);
        Assert.Equal("Trailer 8", second.Name);
    }

    private static async Task<Trailer> TrailerAfter(int delayMs)
    {
        if (delayMs == 0)
        {
            await Task.Yield();
        }
        else
        {
            await Task.Delay(delayMs);
        }

        return new Trailer { Name = "Trailer 7", Axles = 2 };
    }

    private sealed class RecordingHandle : IRenderHandle
    {
        public int RequestRenderCount;

        public Task RequestRender()
        {
            Interlocked.Increment(ref RequestRenderCount);
            return Task.CompletedTask;
        }
    }
}

public sealed class Trailer
{
    public string Name { get; set; } = string.Empty;

    public int Axles { get; set; }
}

/// <summary>The bare edit page: the record read in a hook, "no such" until it is there, then the form.</summary>
public sealed partial class TrailerEditPage : Component
{
    private Trailer? _trailer;

    [SkipFactory] public bool ReadInOnUpdated { get; init; }

    [SkipFactory] public Func<Task<Trailer>> Read { get; init; } = static () => Task.FromResult(new Trailer());

    protected override async Task OnMount()
    {
        if (!ReadInOnUpdated)
        {
            _trailer = await Read();
        }
    }

    protected override async Task OnUpdated()
    {
        if (ReadInOnUpdated)
        {
            _trailer = await Read();
        }
    }

    protected override Component? Render() =>
        _trailer is null
            ? P["no such trailer"]
            : Form.Model(_trailer)[Input.Bind(() => _trailer.Name).Id("name")];
}

/// <summary>A page that hands its form component whichever model it holds now, always under one Key.</summary>
public sealed partial class TrailerFormHost : Component
{
    [SkipFactory] public Trailer? Current { get; private set; }

    [SkipFactory] public EditContext? Context { get; set; }

    [SkipFactory] public int FormsMounted { get; set; }

    // As a page's hook or handler does: the state changes, and the page is drawn again because it did.
    public void Show(Trailer trailer)
    {
        Current = trailer;
        StateHasChanged();
    }

    protected override Component? Render() =>
        Entries.TrailerForm.Key("trailer").Model(Current).Host(this);
}

/// <summary>An app's own form component: the model is a prop, read where the form is drawn.</summary>
public sealed partial class TrailerForm : Component
{
    public Trailer? Model { get; set; }

    public TrailerFormHost? Host { get; set; }

    protected override Task OnMount()
    {
        if (Host is { } host)
        {
            host.FormsMounted++;
        }

        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        Model is null
            ? P["no such trailer"]
            : Form.Model(Model)[
                Input.Bind(() => Model.Name).Id("name"),
                Input.Bind(() => Model.Axles).Id("axles"),
                Test.EditContextProbe(context => Host?.Context = context)
            ];
}
