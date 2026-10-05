using System.Reflection;
using Rask.Core.Dom.Build;

namespace Rask.Core.Tests.Dom;

// HTML's enumerated attributes as the enums the build generates from MDN's keywords: what each renders, what stays a
// string, and the checks the emitter makes on the way.
public partial class KeywordAttributeTests : RaskMarkup
{
    [Fact]
    public void A_keyword_attribute_renders_the_keyword_its_member_names()
    {
        var image = Img.Src("/a.png").Alt("").Loading(Loading.Lazy).Decoding(Decoding.Async).FetchPriority(FetchPriority.High).CrossOrigin(CrossOrigin.UseCredentials);

        var html = image.ToHtml();

        Assert.Contains("loading=\"lazy\"", html, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", html, StringComparison.Ordinal);
        Assert.Contains("fetchpriority=\"high\"", html, StringComparison.Ordinal);
        Assert.Contains("crossorigin=\"use-credentials\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generated_global_keyword_attribute_renders_its_keyword()
    {
        var field = Div.InputMode(InputMode.Numeric).EnterKeyHint(EnterKeyHint.Send).Autocapitalize(Autocapitalize.Words);

        var html = field.ToHtml();

        Assert.Contains("inputmode=\"numeric\"", html, StringComparison.Ordinal);
        Assert.Contains("enterkeyhint=\"send\"", html, StringComparison.Ordinal);
        Assert.Contains("autocapitalize=\"words\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_globals_Element_owns_take_their_keyword_enums()
    {
        var panel = Div.Dir(Dir.Rtl).Popover(Popover.Manual).ContentEditable(ContentEditable.PlaintextOnly);

        var html = panel.ToHtml();

        Assert.Equal("<div dir=\"rtl\" popover=\"manual\" contenteditable=\"plaintext-only\"></div>", html);
    }

    [Fact]
    public void An_attribute_reflecting_an_IDL_enum_takes_that_enum()
    {
        var link = A.Href("/x").ReferrerPolicy(ReferrerPolicy.StrictOriginWhenCrossOrigin);
        var template = Template.ShadowRootMode(ShadowRootMode.Open);

        var html = link.ToHtml() + template.ToHtml();

        Assert.Contains("referrerpolicy=\"strict-origin-when-cross-origin\"", html, StringComparison.Ordinal);
        Assert.Contains("shadowrootmode=\"open\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_submission_override_takes_the_form_attribute_s_keywords()
    {
        var button = Button.Type(ButtonType.Submit).FormMethod(FormMethod.Dialog);

        var html = button.ToHtml();

        Assert.Equal("<button formmethod=\"dialog\" type=\"submit\"></button>", html);
    }

    [Fact]
    public void A_true_false_keyword_pair_is_a_boolean()
    {
        var text = Div.WritingSuggestions(false);

        var html = text.ToHtml();

        Assert.Equal("<div writingsuggestions=\"false\"></div>", html);
    }

    [Theory]
    [InlineData(typeof(HTMLOListElement), "Type")] // "1", "a", "A": no member names
    [InlineData(typeof(HTMLButtonElement), "FormEnctype")] // MIME types
    [InlineData(typeof(HTMLAnchorElement), "Rel")] // a token list
    [InlineData(typeof(HTMLAnchorElement), "Target")] // a browsing context's name
    [InlineData(typeof(HTMLMetaElement), "Name")] // open: other specs add names
    [InlineData(typeof(HTMLInputElement), "Autocomplete")] // open: "section-*"
    public void An_attribute_whose_keywords_are_not_a_closed_set_of_words_stays_a_string(Type element, string property)
    {
        var type = element.GetProperty(property)!.PropertyType;

        var isString = type == typeof(string);

        Assert.True(isString, $"{element.Name}.{property} is {type.Name}");
    }

    [Fact]
    public void Every_keyword_value_is_the_FNV_1a_hash_of_its_text()
    {
        var overloads = typeof(Element).Assembly.GetType("Rask.Core.KeywordText", throwOnError: true)!
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Where(m => m.Name == "Of").ToList();

        var wrong = overloads
            .SelectMany(of => Enum.GetValues(of.GetParameters()[0].ParameterType).Cast<object>()
                .Select(v => (Value: v, Text: (string)of.Invoke(null, [v])!)))
            .Where(x => Convert.ToInt32(x.Value, System.Globalization.CultureInfo.InvariantCulture) != DomValueTypes.Fnv1a(x.Text))
            .Select(x => $"{x.Value.GetType().Name}.{x.Value}")
            .ToList();

        Assert.True(overloads.Count > 20, $"only {overloads.Count} keyword types");
        Assert.Empty(wrong);
    }

    [Fact]
    public void A_keyword_enum_offers_no_chain_step_per_member()
    {
        var names = new[] { "get_Lazy", "get_Eager", "get_Rtl", "get_Numeric", "get_PlaintextOnly", "get_NoReferrer" };

        var steps = typeof(Element).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => names.Contains(m.Name))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.Empty(steps);
    }

    [Fact]
    public void A_hand_written_enum_stands_in_for_the_keyword_type_it_names()
    {
        var partials = DomEmitter.ReadPartials([Repo("src/Rask.Core/InputType.cs")]);

        var keywords = DomEmitter.Emit(Snapshot(), partials).Single(f => f.Key == "Keywords.g.cs").Value;

        Assert.DoesNotContain("enum InputType", keywords, StringComparison.Ordinal);
        Assert.Contains("public enum ButtonType", keywords, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_written_enum_missing_a_keyword_fails_the_build()
    {
        var partials = DomEmitter.ReadPartials([Repo("src/Rask.Core/InputType.cs").Replace("    Week,", "", StringComparison.Ordinal)]);

        var failure = Assert.Throws<DomEmitException>(() => DomEmitter.Emit(Snapshot(), partials));

        Assert.Contains("InputType", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Week", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rask_Web_may_not_declare_a_root_type_named_as_a_keyword_type()
    {
        var files = new List<KeyValuePair<string, string>> { new("Globals.g.cs", "// x\nnamespace Rask.Web;\n\npublic sealed partial class Loading\n{\n}\n") };

        var failure = Assert.Throws<DomEmitException>(() => WebEmitter.RefuseKeywordClashes(DomEmitter.Parse(Snapshot()), files));

        Assert.Contains("Loading", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rask_Web_names_Core_s_enum_for_an_attribute_s_IDL_keywords()
    {
        var snapshot = Snapshot();

        var web = WebEmitter.Emit(snapshot).Single(f => f.Key == "WebValues.g.cs").Value;

        Assert.DoesNotContain("enum ReferrerPolicy", web, StringComparison.Ordinal);
    }

    private static string Snapshot() => Repo("src/Rask.Core/Dom/mdn.snapshot.json");

    private static string Repo(string path)
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return File.ReadAllText(Path.Combine(dir, path));
            }
        }

        throw new InvalidOperationException("Rask.slnx not found above the test output.");
    }
}
