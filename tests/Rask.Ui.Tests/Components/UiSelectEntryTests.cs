namespace Rask.Ui.Tests.Components;

/// <summary>
///     One name for both selects: <c>UiSelect</c> over a value, and over a collection of them.
/// </summary>
/// <remarks>
///     <para>
///     The page never chooses between two component names — the MODEL says which control it is, and the openings
///     are told apart by the type they take. That rests on overload resolution landing the right way for every
///     shape a model realistically declares, which is what these pin.
///     </para>
///     <para>
///     The trap they exist for: a generic <c>Bind&lt;T&gt;(Expression&lt;Func&lt;T&gt;&gt;)</c> matches a
///     <c>List&lt;string&gt;</c> member exactly, so without an opening per collection shape a multi-value field
///     would quietly bind the SINGLE-valued control — a select showing one answer over a field holding many,
///     with nothing reported.
///     </para>
/// </remarks>
public partial class UiSelectEntryTests : global::Rask.Core.RaskMarkup
{
    private sealed class Model
    {
        public string Country { get; set; } = "hu";

        public List<string> Tags { get; set; } = ["core"];

        public IList<string> Picks { get; set; } = ["core"];

        public ICollection<string> Roles { get; set; } = ["core"];

        public HashSet<string> Flags { get; set; } = ["core"];

        public System.Collections.ObjectModel.Collection<string> Marks { get; set; } = ["core"];

        public System.Collections.ObjectModel.ObservableCollection<string> Watched { get; set; } = ["core"];

        public string[] Codes { get; set; } = ["core"];
    }

    private static readonly (string Value, string Text)[] Options =
    [
        ("core", "Rask.Core"), ("ui", "Rask.Ui")
    ];

    // The multi-value control draws a listbox that stays open as answers are picked; the single-valued one is the
    // platform's <select>. The rendered element is what says which control the opening chose.
    private static bool IsMultiple(string html) =>
        html.Contains("aria-multiselectable=\"true\"", StringComparison.Ordinal);

    [Fact]
    public void A_value_member_opens_the_single_select()
    {
        var model = new Model();
        var html = UiSelect.Bind(() => model.Country).Options(Options).Label("Country").ToHtml();

        Assert.Contains("<select", html, StringComparison.Ordinal);
        Assert.False(IsMultiple(html));
    }

    [Fact]
    public void Every_collection_a_model_declares_opens_the_multiple_select()
    {
        var model = new Model();

        Assert.True(IsMultiple(UiSelect.Bind(() => model.Tags).Options(Options).Label("Tags").Native(false).ToHtml()));
        Assert.True(IsMultiple(UiSelect.Bind(() => model.Picks).Options(Options).Label("Picks").Native(false).ToHtml()));
        Assert.True(IsMultiple(UiSelect.Bind(() => model.Roles).Options(Options).Label("Roles").Native(false).ToHtml()));
        Assert.True(IsMultiple(UiSelect.Bind(() => model.Flags).Options(Options).Label("Flags").Native(false).ToHtml()));
        Assert.True(IsMultiple(UiSelect.Bind(() => model.Marks).Options(Options).Label("Marks").Native(false).ToHtml()));
        Assert.True(IsMultiple(UiSelect.Bind(() => model.Watched).Options(Options).Label("Watched").Native(false).ToHtml()));
        Assert.True(IsMultiple(UiSelect.Bind(() => model.Codes).Options(Options).Label("Codes").Native(false).ToHtml()));
    }

    [Fact]
    public void A_controlled_collection_opens_the_multiple_select_too()
    {
        // Values, not Value. A collection expression and a bare null are target-typed, so they fit every
        // collection shape equally — one overload per shape would turn `Value(["core"])` into an
        // ambiguity, and any tie-break that settled `null` would settle it for the single select too.
        // So the controlled collection opening carries its own name and takes the interface alone.
        Assert.True(IsMultiple(
            UiSelect.Values<string>(["core"]).Options(Options).Label("Packages").Native(false).ToHtml()));
        Assert.False(IsMultiple(
            UiSelect.Value("core").Options(Options).Label("Package").Native(false).ToHtml()));
    }

    [Fact]
    public async Task The_widened_bind_still_writes_to_the_models_own_collection()
    {
        // The opening rebuilds the expression around a Convert so it fits ICollection<T>; the accessor strips it,
        // so the property it reads and writes is still the model's List<string>.
        var model = new Model();
        var page = global::Rask.Testing.Test.Render(
            UiSelect.Bind(() => model.Tags).Options(Options).Label("Tags").Native(false));

        var option = System.Text.RegularExpressions.Regex.Match(
            page.Html, "<button id=\"([^\"]+)\"[^>]*role=\"option\"[^>]*>(?:(?!</button>).)*Rask\\.Ui",
            System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEqual("", option);

        await page.On("#" + option).ClickAsync();

        Assert.Equal(["core", "ui"], model.Tags);
    }
}
