using System.CodeDom.Compiler;
using System.Reflection;
using Rask.Core.Live;

#pragma warning disable RASK014 // the reset tests need the very instance they hand to the render context

namespace Rask.Core.Tests.Components;

// A chain that names aria-expanded on one render and not the next: the typed Aria* properties share one pending bit, so
// this is the reset path that bit drives (Component.AriaAttrs.ResetUnwritten).
internal sealed partial class TypedAriaResetHost : Component
{
    internal bool Full = true;

    protected override Component? Render() =>
        Full ? Button.AriaExpanded(true).AriaLabel("Menu")["Menu"] : Button.AriaLabel("Menu")["Menu"];
}

/// <summary>
///     The typed <c>aria-*</c> steps generated from the WAI-ARIA spec (src/Rask.Dom.Tasks/AriaEmitter.cs): each value
///     type, the render order beside the <c>Aria</c> bag, the reset, and what an element naming none of them pays.
/// </summary>
public partial class TypedAriaTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Booleans_render_as_the_words_true_and_false()
    {
        var html = Button.AriaExpanded(false).AriaDisabled(true)["x"].ToHtml();

        Assert.Equal("<button aria-disabled=\"true\" aria-expanded=\"false\">x</button>", html);
    }

    [Fact]
    public void A_boolean_step_with_no_argument_sets_true()
    {
        var html = Span.AriaHidden()["*"].ToHtml();

        Assert.Equal("<span aria-hidden=\"true\">*</span>", html);
    }

    [Fact]
    public void Keywords_render_as_the_spec_spells_them()
    {
        var html = Div.AriaLive(AriaLive.Polite).AriaCurrent(AriaCurrent.Page).AriaHasPopup(AriaHasPopup.Listbox).ToHtml();

        Assert.Equal("<div aria-current=\"page\" aria-haspopup=\"listbox\" aria-live=\"polite\"></div>", html);
    }

    [Fact]
    public void A_token_list_renders_its_keywords_space_separated_in_the_spec_order()
    {
        var html = Div.AriaRelevant(AriaRelevant.Text | AriaRelevant.Additions).ToHtml();

        Assert.Equal("<div aria-relevant=\"additions text\"></div>", html);
    }

    [Fact]
    public void Numbers_render_invariant_whatever_the_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");
        string html;
        try
        {
            html = Div.Role(AriaRole.Slider).AriaValueNow(0.5).AriaValueMax(1).AriaLevel(-2).ToHtml();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("<div role=\"slider\" aria-level=\"-2\" aria-valuemax=\"1\" aria-valuenow=\"0.5\"></div>", html);
    }

    [Fact]
    public void Id_references_are_named_after_the_attribute_not_the_element_list()
    {
        var html = Div.Role(AriaRole.Combobox).AriaLabelledBy("title").AriaDescribedBy("hint err").AriaActiveDescendant("opt-2").ToHtml();

        Assert.Contains("aria-activedescendant=\"opt-2\" aria-describedby=\"hint err\" aria-labelledby=\"title\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_value_reads_back_as_it_was_set()
    {
        var div = Div.AriaChecked(AriaChecked.Mixed).AriaPosInSet(3).AriaExpanded(false).AriaRelevant(AriaRelevant.All).AriaLabel("l");

        Assert.Equal(AriaChecked.Mixed, div.AriaChecked);
        Assert.Equal(3, div.AriaPosInSet);
        Assert.False(div.AriaExpanded);
        Assert.Equal(AriaRelevant.All, div.AriaRelevant);
        Assert.Equal("l", div.AriaLabel);
        Assert.Null(div.AriaLive);
    }

    [Fact]
    public void Setting_null_takes_the_attribute_out()
    {
        var div = Div.AriaLabel("a").AriaExpanded(true).AriaLevel(1);

        var html = div.AriaExpanded(null).AriaLabel(null).ToHtml();

        Assert.Equal("<div aria-level=\"1\"></div>", html);
    }

    [Fact]
    public void Typed_aria_renders_before_the_bag_which_skips_the_keys_it_already_wrote()
    {
        var html = Div.Aria(("label", "from the bag"), ("roledescription", "card")).AriaLabel("typed").AriaBusy(true).ToHtml();

        Assert.Equal("<div aria-busy=\"true\" aria-label=\"typed\" aria-roledescription=\"card\"></div>", html);
    }

    [Fact]
    public void An_element_naming_no_typed_aria_carries_no_store()
    {
        var div = Div.Class("x").Aria("label", "l");

        Assert.Null(div.AriaAttrsInternal);
    }

    [Fact]
    public void Assigning_null_to_an_element_that_never_named_aria_allocates_nothing()
    {
        var div = Div.AriaLabel(null).AriaExpanded(null);

        var globals = div.GlobalAttrsInternal;

        Assert.Null(globals);
    }

    [Fact]
    public void A_step_the_next_render_omits_is_gone_from_the_output()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new TypedAriaResetHost();
        Assert.Contains("aria-expanded=\"true\"", Render(host, sp), StringComparison.Ordinal);

        host.Full = false;
        var html = Render(host, sp);

        Assert.Equal("<button aria-label=\"Menu\">Menu</button>", html);
    }

    [Fact]
    public void Re_supplying_the_same_typed_aria_keeps_every_attribute()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new TypedAriaResetHost();

        Render(host, sp);
        var second = Render(host, sp);

        Assert.Equal("<button aria-expanded=\"true\" aria-label=\"Menu\">Menu</button>", second);
    }

    [Fact]
    public void AriaRole_spells_each_role_as_the_role_attribute_does()
    {
        var roles = typeof(AriaRole).GetFields(BindingFlags.Public | BindingFlags.Static).ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.Equal("alertdialog", roles["Alertdialog"]);
        Assert.Equal("menuitemcheckbox", roles["Menuitemcheckbox"]);
        Assert.All(roles, r => Assert.Equal(r.Key.ToLowerInvariant(), r.Value));
    }

    [Fact]
    public void AriaRole_leaves_out_the_spec_s_abstract_roles()
    {
        var roles = typeof(AriaRole).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.DoesNotContain("widget", roles);
        Assert.DoesNotContain("roletype", roles);
        Assert.Contains("tablist", roles);
    }

    [Fact]
    public void A_keyword_enum_member_s_value_is_the_FNV_1a_hash_of_its_keyword()
    {
        var hash = Fnv1a("polite");

        Assert.Equal((int)AriaLive.Polite, hash);
    }

    [Fact]
    public void A_keyword_enum_gives_no_chain_step_per_keyword()
    {
        // Steps are extension members of the generated setter class: a `.Polite` step would be a property getter there.
        var steps = typeof(RaskMarkup).Assembly.GetTypes()
            .Where(t => t.Name.StartsWith("RaskBuilderSetters", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("AriaLive", steps);
        Assert.DoesNotContain("get_Polite", steps);
        Assert.DoesNotContain("get_Assertive", steps);
    }

    [Fact]
    public void Every_typed_aria_member_carries_the_generator_s_mark()
    {
        var unmarked = typeof(Element).GetProperties()
            .Where(p => p.Name.StartsWith("Aria", StringComparison.Ordinal) && p.Name != nameof(Element.Aria))
            .Where(p => p.GetCustomAttribute<GeneratedCodeAttribute>()?.Tool != "Rask.Dom.Aria")
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(unmarked);
    }

    private static int Fnv1a(string keyword)
    {
        var hash = 2166136261u;
        foreach (var c in keyword)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return unchecked((int)hash);
    }

    private static string Render(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        LiveRenderContext.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }
}
