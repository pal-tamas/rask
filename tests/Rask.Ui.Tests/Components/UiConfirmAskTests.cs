using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>await Ui.Confirm.Ask("…")</c>: a question from code, handed to the page's runtime with the words and the tone
///     the chain set, and its answer handed back.
/// </summary>
/// <remarks>
///     What the browser does with it — the dialog opening, Yes, No, Escape, focus — is <c>ConfirmAskTests</c> in
///     <c>Rask.Server.E2E.Tests</c> and <c>ConfirmWasmTests</c> in <c>Rask.Site.E2E.Tests</c>. These hold the call
///     the runtime is made, and what each of its outcomes becomes.
/// </remarks>
public sealed class UiConfirmAskTests
{
    [Fact]
    public async Task Ask_hands_the_runtime_the_question_and_leaves_the_words_to_the_layouts_dialog()
    {
        var js = new AnsweringJs(true);
        using var work = Ambient.Enter(Services(js));

        var yes = await Ui.Confirm.Ask("Save it anyway?");

        Assert.True(yes);
        Assert.Equal("__raskConfirm.ask", js.Identifier);
        Assert.Equal(["Save it anyway?", null, null, null, null], js.Arguments);
    }

    [Fact]
    public async Task The_words_and_the_tone_set_on_the_chain_are_the_questions_own()
    {
        var js = new AnsweringJs(false);
        using var work = Ambient.Enter(Services(js));

        var yes = await Ui.Confirm.Heading("Km").No("Nem").Yes("Igen").Primary().Ask("Rögzíti a km-t?");

        Assert.False(yes);
        Assert.Equal(["Rögzíti a km-t?", "Km", "Nem", "Igen", "primary"], js.Arguments);
    }

    [Fact]
    public async Task Primary_false_asks_in_the_danger_tone_whatever_the_layout_drew()
    {
        var js = new AnsweringJs(true);
        using var work = Ambient.Enter(Services(js));

        await Ui.Confirm.Primary(false).Ask("Delete it?");

        Assert.Equal("danger", js.Arguments![4]);
    }

    [Fact]
    public async Task A_page_with_no_Ui_Confirm_is_told_what_to_place()
    {
        var js = new AnsweringJs(null);
        using var work = Ambient.Enter(Services(js));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Ui.Confirm.Ask("Save it anyway?"));

        Assert.Contains("found no Ui.Confirm on the page", error.Message, StringComparison.Ordinal);
        Assert.Contains("Place Ui.Confirm in the layout", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_cancelled_while_it_is_asked_is_answered_no()
    {
        var js = new AnsweringJs(true) { Cancels = true };
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var work = Ambient.Enter(Services(js));
        using var token = Ambient.Enter(cancelled.Token);

        var yes = await Ui.Confirm.Ask("Save it anyway?");

        Assert.False(yes);
        Assert.Equal(cancelled.Token, js.Token);
    }

    [Fact]
    public async Task A_page_that_went_away_is_answered_no()
    {
        var js = new AnsweringJs(true) { Disconnects = true };
        using var work = Ambient.Enter(Services(js));

        var yes = await Ui.Confirm.Ask("Save it anyway?");

        Assert.False(yes);
    }

    [Fact]
    public async Task Asked_outside_any_work_it_says_where_to_ask_from()
    {
        using var nothing = Ambient.Enter((IServiceProvider?)null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Ui.Confirm.Ask("Save it anyway?"));

        Assert.Contains("outside an event handler or a lifecycle hook", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_question_is_refused()
    {
        var js = new AnsweringJs(true);
        using var work = Ambient.Enter(Services(js));

        await Assert.ThrowsAsync<ArgumentException>(() => Ui.Confirm.Ask(" "));

        Assert.Null(js.Identifier);
    }

    private static ServiceProvider Services(IJSRuntime js) =>
        new ServiceCollection().AddSingleton(js).BuildServiceProvider();

    // The runtime's side of the call: records it, and answers as the page would.
    private sealed class AnsweringJs(bool? answer) : IJSRuntime
    {
        public bool Cancels { get; init; }

        public bool Disconnects { get; init; }

        public string? Identifier { get; private set; }

        public object?[]? Arguments { get; private set; }

        public CancellationToken Token { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Identifier = identifier;
            Arguments = args;
            Token = cancellationToken;
            if (Cancels)
            {
                throw new TaskCanceledException();
            }

            if (Disconnects)
            {
                throw new JSDisconnectedException("gone");
            }

            return ValueTask.FromResult((TValue)(object?)answer!);
        }
    }
}
