using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;


namespace Rask.Validation.DataAnnotations.Tests;

// Coverage for nested model validation through a single top-of-form DataAnnotationsValidator.
// The reference-based FieldIdentifier scheme means messages for a sub-object property land on
// (subInstance, "Property") — these tests pin that routing across every reachable shape:
// sub-objects, lists, deep chains, cycles, dictionaries, replaced records, per-field re-runs.
public partial class NestedValidationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Validate_SubObjectProperty_FiresAtSubInstanceField()
    {
        var p = new Person { Address = new Address { Street = "" } };
        var ctx = RegisterValidator(p);

        ctx.Validate();

        // The Required error must land on the Address instance, NOT on the root Person.
        Assert.Contains("Street required", ctx.GetValidationMessages(new FieldIdentifier(p.Address, "Street")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Street")));
    }

    [Fact]
    public void Validate_DeepChain_FiresAtTerminalOwner()
    {
        var p = new Person
        {
            Address = new Address { Postal = new PostalInfo { Country = new Country { Code = "" } } }
        };
        var ctx = RegisterValidator(p);

        ctx.Validate();

        Assert.Contains("Code required",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address!.Postal!.Country!, "Code")));
    }

    [Fact]
    public void Validate_ListItems_EachFiresAtOwnInstance()
    {
        var alpha = new LineItem { Name = "", Quantity = 0 };
        var beta = new LineItem { Name = "beta", Quantity = -1 };
        var gamma = new LineItem { Name = "gamma", Quantity = 5 };

        var p = new Person { Items = new List<LineItem> { alpha, beta, gamma } };
        var ctx = RegisterValidator(p);

        ctx.Validate();

        Assert.Contains("Name required", ctx.GetValidationMessages(new FieldIdentifier(alpha, "Name")));
        Assert.Contains("Quantity must be positive",
            ctx.GetValidationMessages(new FieldIdentifier(alpha, "Quantity")));
        Assert.Contains("Quantity must be positive",
            ctx.GetValidationMessages(new FieldIdentifier(beta, "Quantity")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(gamma, "Name")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(gamma, "Quantity")));
    }

    [Fact]
    public void Validate_ArrayItems_Walked()
    {
        var p = new Person { Tags = new[] { new Tag { Label = "" }, new Tag { Label = "ok" } } };
        var ctx = RegisterValidator(p);

        ctx.Validate();

        Assert.Contains("Label required",
            ctx.GetValidationMessages(new FieldIdentifier(p.Tags![0], "Label")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p.Tags![1], "Label")));
    }

    [Fact]
    public void Validate_DictionaryValues_Walked()
    {
        var p = new Person
        {
            Settings = new Dictionary<string, ServerConfig>
            {
                ["smtp"] = new() { Host = "" },
                ["http"] = new() { Host = "api.example.com" }
            }
        };
        var ctx = RegisterValidator(p);

        ctx.Validate();

        Assert.Contains("Host required",
            ctx.GetValidationMessages(new FieldIdentifier(p.Settings!["smtp"], "Host")));
        Assert.Empty(
            ctx.GetValidationMessages(new FieldIdentifier(p.Settings!["http"], "Host")));
    }

    [Fact]
    public void Validate_Cycle_NoInfiniteLoop()
    {
        // Manager points back at p — the walker must visit each node once.
        var manager = new Person { Name = "Boss" };
        var p = new Person { Name = "", Manager = manager };
        manager.Manager = p;

        var ctx = RegisterValidator(p);
        ctx.Validate();

        Assert.Contains("Name required", ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public void Validate_NullSubObject_SkippedCleanly()
    {
        var p = new Person { Name = "Ada", Address = null };
        var ctx = RegisterValidator(p);

        var ok = ctx.Validate();

        Assert.True(ok);
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public void Validate_NullListItem_SkippedCleanly()
    {
        var alpha = new LineItem { Name = "alpha", Quantity = 1 };
        var p = new Person { Items = new List<LineItem> { alpha, null!, new() { Name = "gamma", Quantity = 2 } } };
        var ctx = RegisterValidator(p);

        var ok = ctx.Validate();

        Assert.True(ok);
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public void Validate_SubObjectIValidatableObject_RoutesPerFieldMessages()
    {
        var p = new Person
        {
            Address = new Address
            {
                Street = "Elm",
                Postal = new PostalInfo { Country = new Country { Code = "INVALID", RaiseFormLevel = false } }
            }
        };
        var ctx = RegisterValidator(p);

        ctx.Validate();

        // The IValidatableObject rule on Country emits a per-field error tied to Code.
        Assert.Contains("Code must be 2 letters",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address!.Postal!.Country!, "Code")));
    }

    [Fact]
    public void Validate_SubObjectIValidatableObject_FormLevelRoutesToOwnerEmptyField()
    {
        var p = new Person
        {
            Address = new Address
            {
                Street = "Elm",
                Postal = new PostalInfo { Country = new Country { Code = "NL", RaiseFormLevel = true } }
            }
        };
        var ctx = RegisterValidator(p);

        ctx.Validate();

        // Form-level (empty MemberNames) errors from a sub-object's IValidatableObject attach
        // to that sub-object's (instance, "") slot — they still surface in ValidationSummary
        // (which reads every state) but the owner stays clear.
        Assert.Contains("Country blocked",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address!.Postal!.Country!, string.Empty)));
    }

    [Fact]
    public void ValidateField_NestedField_OnlyTouchesNamedField()
    {
        // Per-field validation at any depth must update only the (owner, name) slot — no
        // bleed onto root, no bleed onto sibling fields of the same owner.
        var p = new Person
        {
            Name = "",
            Address = new Address { Street = "", Postal = new PostalInfo { Country = new Country { Code = "" } } }
        };
        var ctx = RegisterValidator(p);

        ctx.ValidateField(new FieldIdentifier(p.Address!, "Street"));

        Assert.Contains("Street required",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address!, "Street")));
        // Other fields, even on the same owner, stay clean — per-field validation is scoped.
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p.Address!.Postal!.Country!, "Code")));
    }

    [Fact]
    public void ValidateField_ListItemField_OnlyTouchesThatItem()
    {
        var alpha = new LineItem { Name = "", Quantity = 0 };
        var beta = new LineItem { Name = "", Quantity = 0 };
        var p = new Person { Items = new List<LineItem> { alpha, beta } };
        var ctx = RegisterValidator(p);

        ctx.ValidateField(new FieldIdentifier(beta, "Name"));

        Assert.Contains("Name required", ctx.GetValidationMessages(new FieldIdentifier(beta, "Name")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(alpha, "Name")));
    }

    [Fact]
    public void Validate_ReplacedRecord_NewInstanceValidated()
    {
        var first = new LineRecord("", 0);
        var p = new RecordPerson { Items = new List<LineRecord> { first } };
        var ctx = RegisterValidator(p);

        ctx.Validate();
        Assert.Contains("Name required", ctx.GetValidationMessages(new FieldIdentifier(first, "Name")));

        // Replace the record. Re-validate — the *new* record gets a fresh state slot keyed by
        // its own reference; the old slot's messages get cleared by ClearAllMessages.
        ctx.ClearAllMessages();
        var replaced = first with { Name = "ok" };
        p.Items[0] = replaced;
        ctx.Validate();

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(replaced, "Name")));
        // The discarded record's slot has been cleared too.
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(first, "Name")));
    }

    [Fact]
    public async Task FormPipeline_NestedField_FiresOnChange()
    {
        // End-to-end through the Form factory + Input handler dispatch path. Without the
        // model-graph pre-walk in Form.Model's setter, the Input bound to p.Address.Street
        // would resolve to a separate EditContext keyed by p.Address — different from the
        // form's EditContext where DataAnnotationsValidator self-registers — and the per-
        // keystroke ValidateField call would land in an empty context, producing no message.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form(p)[
            DataAnnotationsValidator(),
            Input.Bind(() => p.Address!.Street),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);
        await page.InvokeAsync(changeId!, "{\"value\":\"\"}");

        Assert.NotNull(captured);
        Assert.Same(p, captured!.Model);
        Assert.Contains("Street required",
            captured.GetValidationMessages(new FieldIdentifier(p.Address!, "Street")));
    }

    [Fact]
    public async Task FormPipeline_NestedField_BlurSurfacesMessageInRenderedHtml()
    {
        // End-to-end through the Form factory + Input handler dispatch path, asserting the
        // [Required] error surfaces in the post-blur HTML via ValidationMessage. This catches
        // the handler/display context split that FormPipeline_NestedField_FiresOnChange
        // misses by reading the EditContext directly instead of going through the renderer.
        var p = new Person { Name = "Ada", Address = new Address { Street = "" } };

        var page = RaskTest.Render(() => Form(p)[
            DataAnnotationsValidator(),
            Input.Bind(() => p.Address!.Street),
            ValidationMessage(() => p.Address!.Street,
                msgs => [.. msgs.Select((m, i) => Div(Class: "err", Key: i)[m])])
        ]);

        var initial = page.Html;
        Assert.DoesNotContain("Street required", initial);

        var changeId = page.HandlerId("change")!;
        var afterBlur = await page.InvokeAsync(changeId, "{\"value\":\"\"}");

        Assert.Contains("Street required", afterBlur);
    }

    private sealed class Person
    {
        [Required(ErrorMessage = "Name required")]
        public string Name { get; set; } = "Ada";

        public Address? Address { get; set; }
        public List<LineItem>? Items { get; set; }
        public Tag[]? Tags { get; set; }
        public Dictionary<string, ServerConfig>? Settings { get; set; }
        public Person? Manager { get; set; }
    }

    private new sealed class Address
    {
        [Required(ErrorMessage = "Street required")]
        public string Street { get; set; } = "";

        public PostalInfo? Postal { get; set; }
    }

    private sealed class PostalInfo
    {
        public Country? Country { get; set; }
    }

    private sealed class Country : IValidatableObject
    {
        [Required(ErrorMessage = "Code required")]
        public string Code { get; set; } = "";

        public bool RaiseFormLevel { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!string.IsNullOrEmpty(Code) && Code.Length != 2)
            {
                yield return new ValidationResult("Code must be 2 letters", new[] { nameof(Code) });
            }

            if (RaiseFormLevel)
            {
                yield return new ValidationResult("Country blocked");
            }
        }
    }

    private sealed class LineItem
    {
        [Required(ErrorMessage = "Name required")]
        public string Name { get; set; } = "";

        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be positive")]
        public int Quantity { get; set; }
    }

    private sealed class Tag
    {
        [Required(ErrorMessage = "Label required")]
        public string Label { get; set; } = "";
    }

    private sealed class ServerConfig
    {
        [Required(ErrorMessage = "Host required")]
        public string Host { get; set; } = "";
    }

    private sealed record LineRecord(
        [property: Required(ErrorMessage = "Name required")]
        string Name,
        int Quantity);

    private sealed class RecordPerson
    {
        public List<LineRecord> Items { get; set; } = new();
    }
}
