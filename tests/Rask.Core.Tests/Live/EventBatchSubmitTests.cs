using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Forms;
using Rask.Core.Live;

#pragma warning disable RASK014 // a test-defined root is rendered as a session renders its own

namespace Rask.Core.Tests.Live;

/// <summary>
///     A batch is read once before its first event runs: a form whose submit is in it is told, so the values
///     ahead of that submit leave the store's rules to it. A form with no submit in the batch is told nothing.
/// </summary>
public partial class EventBatchSubmitTests
{
    private static JsonElement Batch(params object[] events) =>
        JsonSerializer.SerializeToElement(events);

    [Fact]
    public void Only_the_form_whose_submit_is_in_the_batch_is_told_that_it_follows()
    {
        var page = new TwoFormsPage();
        var ids = Regex.Matches(page.RenderAsLiveRoot(), "data-rask-on-submit=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        var told = EventBatch.AnnounceSubmits(page, Batch(new { id = "h999", type = "change", value = "x" }, new { id = ids[1], type = "submit" }));

        Assert.Equal(2, ids.Count);
        Assert.False(page.First.SubmitFollows);
        Assert.True(page.Second.SubmitFollows);
        Assert.Same(page.Second, Assert.Single(told!));
    }

    [Fact]
    public void A_batch_with_no_submit_or_a_click_on_the_forms_id_tells_no_form()
    {
        var page = new TwoFormsPage();
        var id = Regex.Match(page.RenderAsLiveRoot(), "data-rask-on-submit=\"([^\"]+)\"").Groups[1].Value;

        var told = EventBatch.AnnounceSubmits(page, Batch(new { id, type = "click" }, new { id = "h999", type = "submit" }));

        Assert.Null(told);
        Assert.False(page.First.SubmitFollows);
    }

    [Fact]
    public void What_was_announced_is_withdrawn_when_the_batch_is_over()
    {
        var page = new TwoFormsPage();
        var id = Regex.Match(page.RenderAsLiveRoot(), "data-rask-on-submit=\"([^\"]+)\"").Groups[1].Value;
        var told = EventBatch.AnnounceSubmits(page, Batch(new { id, type = "submit" }));

        EventBatch.Withdraw(told);

        Assert.False(page.First.SubmitFollows);
    }
}

/// <summary>Two forms over two models, each with its own submit.</summary>
public sealed partial class TwoFormsPage : Component
{
    private readonly Named _first = new();
    private readonly Named _second = new();

    public TwoFormsPage()
    {
        First = new EditContext(_first);
        Second = new EditContext(_second);
    }

    /// <summary>The first form's context.</summary>
    internal EditContext First { get; }

    /// <summary>The second form's context.</summary>
    internal EditContext Second { get; }

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Form.Model(_first).Context(First).OnSubmit(static () => { })[Input.Bind(() => _first.Name)],
        Form.Model(_second).Context(Second).OnSubmit(static () => { })[Input.Bind(() => _second.Name)]
    ];

    private sealed class Named
    {
        public string Name { get; set; } = "";
    }
}
