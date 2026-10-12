using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// One question says whether a field must be filled — the model's type registered it, or the member is
// [Required] — and a bound control announces the answer as aria-required. Every RegisterRequiredFields call of
// this assembly is in this class, whose tests run one after another: the count of questions below relies on it.
public partial class RequiredFieldTests : global::Rask.Core.RaskMarkup
{
    static RequiredFieldTests()
    {
        RaskValidation.RegisterRequiredFields(typeof(Parcel), Parcel.IsRequired);
        RaskValidation.RegisterRequiredFields(typeof(Row), Row.IsRequired);
        RaskValidation.RegisterRequiredFields(typeof(Money), Money.IsRequired);
    }

    [Fact]
    public void A_field_its_models_type_registered_is_required_with_no_attribute_on_it()
    {
        var model = typeof(Parcel);

        var required = RaskValidation.IsRequired(model, nameof(Parcel.Recipient));

        Assert.True(required);
    }

    [Fact]
    public void A_member_that_carries_the_attribute_is_required_with_nothing_registered()
    {
        var registered = typeof(Parcel);
        var unregistered = typeof(Letter);

        var onARegisteredType = RaskValidation.IsRequired(registered, nameof(Parcel.Street));
        var onAnUnregisteredType = RaskValidation.IsRequired(unregistered, nameof(Letter.Address));

        Assert.True(onARegisteredType);
        Assert.True(onAnUnregisteredType);
    }

    [Fact]
    public void A_field_neither_a_registration_nor_an_attribute_speaks_for_is_not_required()
    {
        var registered = typeof(Parcel);
        var unregistered = typeof(Letter);

        var onARegisteredType = RaskValidation.IsRequired(registered, nameof(Parcel.Note));
        var onAnUnregisteredType = RaskValidation.IsRequired(unregistered, nameof(Letter.Greeting));

        Assert.False(onARegisteredType);
        Assert.False(onAnUnregisteredType);
    }

    [Fact]
    public void A_registration_on_a_base_type_covers_a_model_derived_from_it()
    {
        var derived = typeof(InsuredParcel);

        var inherited = RaskValidation.IsRequired(derived, nameof(Parcel.Recipient));
        var attributed = RaskValidation.IsRequired(derived, nameof(Parcel.Street));
        var own = RaskValidation.IsRequired(derived, nameof(InsuredParcel.Value));

        Assert.True(inherited);
        Assert.True(attributed);
        Assert.False(own);
    }

    [Fact]
    public void A_path_into_a_row_asks_what_the_rows_own_type_registered()
    {
        var model = typeof(Parcel);

        var inAList = RaskValidation.IsRequired(model, "Rows[2].Weight");
        var inAnArray = RaskValidation.IsRequired(model, "Spare[0].Weight");
        var inADictionary = RaskValidation.IsRequired(model, "Named[\"big\"].Weight");
        var unregisteredOnTheRow = RaskValidation.IsRequired(model, "Rows[2].Label");

        Assert.True(inAList);
        Assert.True(inAnArray);
        Assert.True(inADictionary);
        Assert.False(unregisteredOnTheRow);
    }

    [Fact]
    public void A_path_through_a_nested_object_asks_the_nested_objects_type_and_looks_through_a_nullable()
    {
        var model = typeof(Parcel);

        var nested = RaskValidation.IsRequired(model, "Price.Amount");
        var throughANullable = RaskValidation.IsRequired(model, "Discount.Amount");
        var deep = RaskValidation.IsRequired(model, "Rows[0].Cost.Amount");

        Assert.True(nested);
        Assert.True(throughANullable);
        Assert.True(deep);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Missing")]
    [InlineData("Missing.Weight")]
    [InlineData("Rows[2]")]
    [InlineData("Recipient[0].Weight")]
    [InlineData("Rows[2")]
    public void A_path_that_names_no_property_is_not_required(string path)
    {
        var model = typeof(Parcel);

        var required = RaskValidation.IsRequired(model, path);

        Assert.False(required);
    }

    [Fact]
    public void Registering_a_type_again_replaces_what_it_had_and_what_was_answered_under_it()
    {
        RaskValidation.RegisterRequiredFields(typeof(Replaced), name => name == nameof(Replaced.First));
        var before = (RaskValidation.IsRequired(typeof(Replaced), "First"), RaskValidation.IsRequired(typeof(Replaced), "Second"));

        RaskValidation.RegisterRequiredFields(typeof(Replaced), name => name == nameof(Replaced.Second));
        var after = (RaskValidation.IsRequired(typeof(Replaced), "First"), RaskValidation.IsRequired(typeof(Replaced), "Second"));

        Assert.Equal((true, false), before);
        Assert.Equal((false, true), after);
    }

    [Fact]
    public void A_field_asked_about_on_every_render_puts_the_question_to_its_registration_once()
    {
        var asked = 0;
        RaskValidation.RegisterRequiredFields(typeof(Counted), _ => Interlocked.Increment(ref asked) > 0);

        for (var render = 0; render < 100; render++)
        {
            RaskValidation.IsRequired(typeof(Counted), nameof(Counted.Name));
        }

        Assert.Equal(1, asked);
    }

    [Fact]
    public void A_field_asked_about_again_allocates_nothing()
    {
        RaskValidation.IsRequired(typeof(Parcel), nameof(Parcel.Recipient));
        RaskValidation.IsRequired(typeof(Letter), nameof(Letter.Greeting));
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var render = 0; render < 1_000; render++)
        {
            RaskValidation.IsRequired(typeof(Parcel), nameof(Parcel.Recipient));
            RaskValidation.IsRequired(typeof(Letter), nameof(Letter.Greeting));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Fields_asked_about_from_many_threads_while_types_register_all_get_the_registered_answer()
    {
        var wrong = 0;

        Parallel.For(0, 64, i =>
        {
            RaskValidation.RegisterRequiredFields(Crowd[i % Crowd.Length], name => name == "Name");
            for (var ask = 0; ask < 200; ask++)
            {
                if (!RaskValidation.IsRequired(typeof(Parcel), "Rows[1].Weight")
                    || !RaskValidation.IsRequired(typeof(Parcel), nameof(Parcel.Recipient))
                    || RaskValidation.IsRequired(typeof(Parcel), nameof(Parcel.Note)))
                {
                    Interlocked.Increment(ref wrong);
                }
            }
        });

        Assert.Equal(0, wrong);
        Assert.All(Crowd, type => Assert.True(RaskValidation.IsRequired(type, "Name")));
    }

    [Fact]
    public void Registering_takes_a_type_and_a_question_and_asking_takes_a_type_and_a_path()
    {
        Func<string, bool> never = _ => false;

        var noType = Record.Exception(() => RaskValidation.RegisterRequiredFields(null!, never));
        var noQuestion = Record.Exception(() => RaskValidation.RegisterRequiredFields(typeof(Letter), null!));
        var askedOfNoType = Record.Exception(() => RaskValidation.IsRequired(null!, "Name"));
        var askedOfNoPath = Record.Exception(() => RaskValidation.IsRequired(typeof(Letter), null!));

        Assert.All([noType, noQuestion, askedOfNoType, askedOfNoPath], thrown => Assert.IsType<ArgumentNullException>(thrown));
    }

    [Fact]
    public void A_bound_input_says_aria_required_for_a_field_its_models_type_registered()
    {
        var parcel = new Parcel();

        var html = Page.Render(() => Input.Bind(() => parcel.Recipient).Id("to").TabIndex(2)).Render();

        Assert.Contains("""<input id="to" tabindex="2" aria-required="true" type="text" """, html, StringComparison.Ordinal);
        Assert.DoesNotContain(" required", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_select_says_aria_required_for_a_field_its_models_type_registered()
    {
        var parcel = new Parcel();

        var html = Page.Render(() => Select.Bind(() => parcel.Recipient).Id("to").TabIndex(2)[Option.Value("ada")["Ada"]]).Render();

        Assert.Contains("""<select id="to" tabindex="2" aria-required="true" name="Recipient" """, html, StringComparison.Ordinal);
        Assert.DoesNotContain(" required", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_textarea_says_aria_required_for_a_field_its_models_type_registered()
    {
        var parcel = new Parcel();

        var html = Page.Render(() => Textarea.Bind(() => parcel.Recipient).Id("to").TabIndex(2)).Render();

        Assert.Contains("""<textarea id="to" tabindex="2" aria-required="true" name="Recipient" """, html, StringComparison.Ordinal);
        Assert.DoesNotContain(" required", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_bound_control_says_aria_required_for_a_member_that_carries_the_attribute()
    {
        var letter = new Letter();

        var html = Page.Render(() => Div[
            Input.Bind(() => letter.Address),
            Select.Bind(() => letter.Address)[Option.Value("a")["A"]],
            Textarea.Bind(() => letter.Address)
        ]).Render();

        Assert.Equal(3, Announced().Count(html));
    }

    [Fact]
    public void No_bound_control_says_aria_required_for_a_field_nothing_speaks_for()
    {
        var letter = new Letter();

        var html = Page.Render(() => Div[
            Input.Bind(() => letter.Greeting),
            Select.Bind(() => letter.Greeting)[Option.Value("a")["A"]],
            Textarea.Bind(() => letter.Greeting)
        ]).Render();

        Assert.DoesNotContain("aria-required", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aria_required_the_app_wrote_on_the_control_is_the_one_that_is_written()
    {
        var parcel = new Parcel();

        var html = Page.Render(() => Div[
            Input.Bind(() => parcel.Recipient).AriaRequired(false),
            Select.Bind(() => parcel.Recipient).Aria("required", "false")[Option.Value("a")["A"]],
            Textarea.Bind(() => parcel.Recipient).Attributes(new Dictionary<string, string?> { ["aria-required"] = "false" })
        ]).Render();

        Assert.Equal(3, Regex.Count(html, "aria-required=\"false\""));
        Assert.DoesNotContain("aria-required=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_bound_in_place_says_aria_required_by_what_its_own_type_registered()
    {
        var parcel = new Parcel();

        var html = Page.Render(() => Div[
            Input.Bind(() => parcel.Rows[1].Weight).Id("weight"),
            Input.Bind(() => parcel.Rows[1].Label).Id("label")
        ]).Render();

        Assert.Contains("""<input id="weight" aria-required="true" """, html, StringComparison.Ordinal);
        Assert.Equal(1, Announced().Count(html));
    }

    [Fact]
    public void A_radio_and_a_range_say_no_aria_required_because_their_roles_have_none()
    {
        var parcel = new Parcel();

        var html = Page.Render(() => Div[
            Input.Bind(() => parcel.Recipient).Type(InputType.Radio),
            Input.Bind(() => parcel.Recipient).Type(InputType.Range),
            Input.Bind(() => parcel.Recipient).Type(InputType.Hidden)
        ]).Render();

        Assert.DoesNotContain("aria-required", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_seam_and_the_attribute_validator_read_the_required_attribute_under_src()
    {
        var src = Path.Combine(RepoRoot(), "src");
        string[] allowed =
        [
            Path.Combine("Rask.Core", "Forms", "RequiredFields.cs"),
            Path.Combine("Rask.Core", "Forms", "RaskValidation.cs"),
            Path.Combine("Rask.Core", "Forms", "DataAnnotationsFieldValidator.cs"),
            // A source generator: it copies the attribute's NAME into generated text and reads nothing at run time.
            Path.Combine("Rask.Batteries.Generators", "ModelInputGenerator.Model.cs"),
        ];

        var readers = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(src, file))
            .Where(file => !Generated().IsMatch(file) && !allowed.Contains(file, StringComparer.Ordinal))
            .Where(file => ReadsTheAttribute().IsMatch(File.ReadAllText(Path.Combine(src, file))))
            .ToArray();

        Assert.True(
            readers.Length == 0,
            "These read RequiredAttribute themselves; ask RaskValidation.IsRequired, the one question, instead:\n"
            + string.Join("\n", readers));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No Rask.slnx above {AppContext.BaseDirectory}.");
    }

    private static readonly Type[] Crowd = [typeof(CrowdA), typeof(CrowdB), typeof(CrowdC), typeof(CrowdD)];

    [GeneratedRegex("aria-required=\"true\"")]
    private static partial Regex Announced();

    // The type itself, not Blazor's EditorRequiredAttribute.
    [GeneratedRegex(@"(?<![A-Za-z])RequiredAttribute\b")]
    private static partial Regex ReadsTheAttribute();

    [GeneratedRegex(@"[\\/](obj|bin)[\\/]")]
    private static partial Regex Generated();

    // What a generator emits for a model: one static switch over the names its type requires.
    private class Parcel
    {
        public string Recipient { get; set; } = "";

        [Required] public string Street { get; set; } = "";

        public string Note { get; set; } = "";

        public List<Row> Rows { get; set; } = [new(), new(), new()];

        public Row[] Spare { get; set; } = [new()];

        public Dictionary<string, Row> Named { get; set; } = [];

        public Money Price { get; set; }

        public Money? Discount { get; set; }

        internal static bool IsRequired(string property) => property switch
        {
            nameof(Recipient) => true,
            _ => false,
        };
    }

    private sealed class InsuredParcel : Parcel
    {
        public decimal Value { get; set; }
    }

    private sealed class Row
    {
        public decimal Weight { get; set; }

        public string Label { get; set; } = "";

        public Money Cost { get; set; }

        internal static bool IsRequired(string property) => property == nameof(Weight);
    }

    private struct Money
    {
        public decimal Amount { get; set; }

        internal static bool IsRequired(string property) => property == nameof(Amount);
    }

    private sealed class Letter
    {
        [Required] public string Address { get; set; } = "";

        public string Greeting { get; set; } = "";
    }

    private sealed class Replaced
    {
        public string First { get; set; } = "";

        public string Second { get; set; } = "";
    }

    private sealed class Counted
    {
        public string Name { get; set; } = "";
    }

    private sealed class CrowdA
    {
        public string Name { get; set; } = "";
    }

    private sealed class CrowdB
    {
        public string Name { get; set; } = "";
    }

    private sealed class CrowdC
    {
        public string Name { get; set; } = "";
    }

    private sealed class CrowdD
    {
        public string Name { get; set; } = "";
    }
}
